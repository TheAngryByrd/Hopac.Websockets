namespace Hopac.Websockets


open System.IO
[<AutoOpen>]
module Infixes =
    let (^) = (<|)


module Hopac =
    open System
    open Hopac
    open Hopac.Infixes
    open System.Threading
    module Alt =
        let using (disposable : #IDisposable) (alt :  #IDisposable -> #Alt<'a>)  =
            alt disposable
            |> fun a -> Alt.tryFinallyFun a disposable.Dispose

        let fromCT (ct : CancellationToken) =
            let cancelled = IVar()
            using
                (ct.Register(fun () -> cancelled *<=  () |> start))
                ^ fun _ -> cancelled

module Stream =
    open System
    open Hopac
    let read buffer offset count (stream : #IO.Stream) =
        Alt.fromTask ^ fun ct ->
            stream.ReadAsync(buffer, offset, count, ct)

    let write buffer offset count (stream : #IO.Stream) =
        Alt.fromUnitTask ^ fun ct ->
            stream.WriteAsync(buffer, offset, count, ct)

module WebSocket =
    open System
    open Hopac
    open System.Net.WebSockets
    open Hopac.Infixes

    type BufferSize = int
    // https://referencesource.microsoft.com/#System/net/System/Net/WebSockets/WebSocketHelpers.cs,285b8b64a4da6851
    [<Literal>]
    let defaultBufferSize = 16384 // (16 * 1024)

    let receive buffer  (websocket : #WebSocket)=
        Alt.fromTask ^ fun ct ->
            websocket.ReceiveAsync(buffer,ct)

    let send buffer messageType endOfMessage (websocket : #WebSocket) =
        Alt.fromUnitTask ^ fun ct ->
            websocket.SendAsync(buffer, messageType, endOfMessage, ct)


    let close status message  (websocket : #WebSocket) =
        Alt.fromUnitTask ^ fun ct ->
            websocket.CloseAsync(status,message,ct)

    let isWebsocketOpen (socket : #WebSocket) =
        socket.State = WebSocketState.Open

    let sendMessage bufferSize messageType (message : #IO.Stream) (socket : #WebSocket) =
        Alt.withNackJob ^ fun nack -> job {
            let buffer = Array.create (bufferSize) Byte.MinValue

            let rec sendMessage' () = job {
                let! read =
                    message |> Stream.read buffer 0 buffer.Length
                    <|> nack ^-> fun () -> 0
                if read > 0 then
                    do!
                        (socket |> send (ArraySegment(buffer |> Array.take read))  messageType false)
                        <|> nack
                    return! sendMessage'()
                else
                    do!
                        (socket |> send (ArraySegment(buffer |> Array.take read))  messageType true)
                        <|> nack
            }

            do! sendMessage'()
            return Alt.unit()
        }

    let UTF8toMemoryStream (text : string) =
        new IO.MemoryStream(Text.Encoding.UTF8.GetBytes text)

    let sendMessageAsUTF8 text socket =
         Alt.using (UTF8toMemoryStream text)
            ^ fun stream ->
                sendMessage defaultBufferSize WebSocketMessageType.Text stream socket

    let readMessage bufferSize messageType (stream : IO.Stream) (socket : #WebSocket) =
        Alt.withNackJob ^ fun nack -> job {
            let buffer = new ArraySegment<Byte>( Array.create (bufferSize) Byte.MinValue)

            let rec readTillEnd' () = job {
                let! (result : WebSocketReceiveResult option) =
                    ((socket |> receive buffer) ^-> Some)
                    <|> (nack ^-> fun _ -> None)
                match result with
                | Some result ->
                    if result.MessageType <> messageType then return ()
                    do! stream |> Stream.write buffer.Array buffer.Offset  result.Count
                        <|> nack
                    if result.EndOfMessage then
                        return ()
                    else return! readTillEnd' ()
                | None ->
                    return ()
            }
            do! readTillEnd' ()
            return Alt.unit()
        }

    let memoryStreamToUTF8 (stream : IO.MemoryStream) =
        stream.Seek(0L,IO.SeekOrigin.Begin) |> ignore
        stream.ToArray()
        |> Text.Encoding.UTF8.GetString
        |> fun s -> s.TrimEnd(char 0)

    let readMessageAsUTF8 socket =
        Alt.using (new MemoryStream())
        ^ fun stream ->
            readMessage defaultBufferSize WebSocketMessageType.Text stream socket
            ^-> fun _ -> stream |> memoryStreamToUTF8




    type SendMessage = BufferSize * WebSocketMessageType * IO.Stream  * Ch<unit> * Promise<unit>
    type ReceiveMessage= BufferSize * WebSocketMessageType * IO.Stream   * Ch<unit> * Promise<unit>
    type CloseMessage = WebSocketCloseStatus * string * Ch<unit> * Promise<unit>

    type WebsocketThreadSafe = {
        sendCh : Ch<SendMessage>
        receiveCh : Ch<ReceiveMessage>
        closeCh : Ch<CloseMessage>
    }

    module WebsocketThreadSafe =
        let createFromWebSocket (webSocket : WebSocket) =

            let self = {
                sendCh = Ch ()
                receiveCh = Ch ()
                closeCh = Ch ()
            }

            let send () =
                self.sendCh ^=> fun (bufferSize, messageType, stream, reply, nack) -> job {
                    do! (sendMessage bufferSize messageType stream webSocket ^=> Ch.give reply )
                        <|> nack
                }

            let receive () =
                self.receiveCh ^=> fun (bufferSize, messageType, stream, reply, nack) -> job {
                    do! (readMessage bufferSize messageType stream webSocket  ^=> Ch.give reply )
                        <|> nack
                }

            let close () =
                self.closeCh ^=> fun (status, message, reply, nack) -> job {
                    do! (close status message webSocket ^=> Ch.give reply )
                        <|> nack
                }

            let receiveProc = Job.delay <| fun () ->
                receive ()
            let sendProc = Job.delay <| fun () ->
                send () <|> close ()

            Job.foreverServer sendProc
            >>=. Job.foreverServer receiveProc
            >>-. self


        let send wsts bufferSize messageType stream =
            wsts.sendCh *<+->- fun reply nack ->
                (bufferSize, messageType, stream, reply,nack)

        let sendDefaultBufferSize wsts messageType stream  =
            send wsts defaultBufferSize messageType stream

        let sendText wsts stream =
            sendDefaultBufferSize wsts WebSocketMessageType.Text stream

        let sendUTF8String (wsts : WebsocketThreadSafe) (text : string) =
            Alt.using
                (UTF8toMemoryStream text)
                ^ fun ms ->  sendText wsts ms

        let receive wsts bufferSize messageType stream =
            wsts.receiveCh *<+->- fun reply nack ->
                (bufferSize, messageType, stream, reply,nack)

        let receiveDefaultBufferSize wsts messageType stream  =
            receive wsts defaultBufferSize messageType stream

        let receiveText wsts stream  =
            receiveDefaultBufferSize wsts WebSocketMessageType.Text stream

        let receiveUTF8String (wsts : WebsocketThreadSafe)  =
            Alt.using
                (new IO.MemoryStream())
                ^ fun stream ->
                    receiveText wsts stream
                    ^-> fun () ->
                        stream |> memoryStreamToUTF8 //Remove null terminator

        let close wsts status message =
            wsts.closeCh *<+->- fun reply nack ->
                (status, message, reply, nack)
