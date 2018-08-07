namespace Hopac.Websockets

open System
open Hopac
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

    module Infixes =
        let ( *<-->= ) qCh rCh2n2qJ = Alt.withNackJob <| fun nack ->
          let rCh = IVar<_> ()
          rCh2n2qJ rCh nack >>= fun q ->
          qCh *<+ q >>-.
          rCh

        let ( *<-->- ) qCh rCh2n2q =
            qCh *<-->= fun rCh n -> rCh2n2q rCh n |> Job.result
[<AutoOpen>]
module Stream =
    open System
    open Hopac

    let read buffer offset count (stream : #IO.Stream) =
        Alt.fromTask ^ fun ct ->
            stream.ReadAsync(buffer, offset, count, ct)

    let write buffer offset count (stream : #IO.Stream) =
        Alt.fromUnitTask ^ fun ct ->
            stream.WriteAsync(buffer, offset, count, ct)

    type System.IO.Stream with
        member stream.ReadJob (buffer: byte[], ?offset, ?count) =
            let offset = defaultArg offset 0
            let count  = defaultArg count buffer.Length
            read buffer offset count stream

        member stream.WriteJob (buffer: byte[], ?offset, ?count) =
            let offset = defaultArg offset 0
            let count  = defaultArg count buffer.Length
            write buffer offset count stream

    type System.IO.MemoryStream with
        static member UTF8toMemoryStream (text : string) =
            new IO.MemoryStream(Text.Encoding.UTF8.GetBytes text)

        static member ToUTF8String (stream : IO.MemoryStream) =
            stream.Seek(0L,IO.SeekOrigin.Begin) |> ignore //ensure start of stream
            stream.ToArray()
            |> Text.Encoding.UTF8.GetString
            |> fun s -> s.TrimEnd(char 0)

        member stream.ToUTF8String () =
            stream |> System.IO.MemoryStream.ToUTF8String




module WebSocket =
    open System
    open Hopac
    open System.Net.WebSockets
    open Hopac.Infixes

    /// Size of the buffer used when sending messages over the socket
    type BufferSize = int

    /// (16 * 1024) = 16384
    /// https://referencesource.microsoft.com/#System/net/System/Net/WebSockets/WebSocketHelpers.cs,285b8b64a4da6851
    [<Literal>]
    let defaultBufferSize  : BufferSize = 16384 // (16 * 1024)

    /// A Hopac Alt version of ReceiveAsync
    /// Alt: https://hopac.github.io/Hopac/Hopac.html#def:type%20Hopac.Alt
    /// ReceiveAsync: https://docs.microsoft.com/en-us/dotnet/api/system.net.websockets.websocket.receiveasync?view=netcore-2.0
    let receive (buffer : ArraySegment<byte>)  (websocket : #WebSocket)=
        Alt.fromTask ^ fun ct ->
            websocket.ReceiveAsync(buffer,ct)

    /// A Hopac Alt version of SendAsync
    /// Alt: https://hopac.github.io/Hopac/Hopac.html#def:type%20Hopac.Alt
    /// SendAsync: https://docs.microsoft.com/en-us/dotnet/api/system.net.websockets.websocket.sendasync?view=netcore-2.0
    let send (buffer : ArraySegment<byte>) messageType endOfMessage (websocket : #WebSocket) =
        Alt.fromUnitTask ^ fun ct ->
            websocket.SendAsync(buffer, messageType, endOfMessage, ct)

    /// A Hopac Alt version of CloseAsync
    /// Grace approach to shutting down.  Use when you want to other end to acknowledge the close.
    /// Alt: https://hopac.github.io/Hopac/Hopac.html#def:type%20Hopac.Alt
    /// CloseAsync: https://docs.microsoft.com/en-us/dotnet/api/system.net.websockets.websocket.closeasync?view=netcore-2.0
    let close status message  (websocket : #WebSocket) =
        Alt.fromUnitTask ^ fun ct ->
            websocket.CloseAsync(status,message,ct)

    /// A Hopac Alt version of CloseOutputAsync
    /// Hard approach to shutting down.  Useful when you don't want the other end to acknowledge the close or this end has received a close notification and want to acknowledge the close.
    /// Alt: https://hopac.github.io/Hopac/Hopac.html#def:type%20Hopac.Alt
    /// CloseOutputAsync: https://docs.microsoft.com/en-us/dotnet/api/system.net.websockets.websocket.closeoutputasync?view=netcore-2.0
    let closeOutput status message (websocket : #WebSocket) =
        Alt.fromUnitTask ^ fun ct ->
            websocket.CloseOutputAsync(status,message,ct)

    let isWebsocketOpen (socket : #WebSocket) =
        socket.State = WebSocketState.Open

    /// Sends a whole message to the websocket read from the given stream
    let sendMessage bufferSize messageType (readableStream : #IO.Stream) (socket : #WebSocket) =
        Alt.withNackJob ^ fun nack ->
            Promise.start ^ job {
                let buffer = Array.create (bufferSize) Byte.MinValue

                let rec sendMessage' () = job {
                    let! read =
                        readableStream |> Stream.read buffer 0 buffer.Length
                        <|> nack ^-> fun () -> 0
                    if read > 0 then
                        do!
                            (socket |> send (ArraySegment(buffer |> Array.take read))  messageType false)
                            <|> nack
                        return! sendMessage'()
                    else
                        do!
                            (socket |> send (ArraySegment(Array.empty))  messageType true)
                            <|> nack
                }
                do! sendMessage'()
        }

    /// Sends the UTF8 string as a whole websocket message
    let sendMessageAsUTF8 text socket =
         Alt.using (IO.MemoryStream.UTF8toMemoryStream text)
            ^ fun stream ->
                sendMessage defaultBufferSize WebSocketMessageType.Text stream socket

    /// Receives a whole message written to the given stream
    /// Attempts to handle closes gracefully
    let receiveMessage bufferSize messageType (writeableStream : IO.Stream) (socket : WebSocket) =
        Alt.withNackJob ^ fun nack ->
            Promise.start ^ job {
                let buffer = new ArraySegment<Byte>( Array.create (bufferSize) Byte.MinValue)

                let rec readTillEnd' () = job {
                    let! (result : WebSocketReceiveResult option) =
                        ((socket |> receive buffer) ^-> Some)
                        <|> (nack ^-> fun _ -> None)
                    match result with
                    | Some result when result.MessageType = WebSocketMessageType.Close || socket.State = WebSocketState.CloseReceived ->
                        // printfn "Close received! %A - %A" socket.CloseStatus socket.CloseStatusDescription
                        do! closeOutput WebSocketCloseStatus.NormalClosure "Close received" socket
                    | Some result ->
                        // printfn "result.MessageType -> %A" result.MessageType
                        if result.MessageType <> messageType then return ()
                        do! writeableStream |> Stream.write buffer.Array buffer.Offset  result.Count
                            <|> nack
                        if result.EndOfMessage then
                            return ()
                        else return! readTillEnd' ()
                    | None ->
                        return ()
                }
                do! readTillEnd' ()
        }

    /// Receives a whole message as a utf8 string
    let receiveMessageAsUTF8 socket =
        Alt.using (new IO.MemoryStream())
        ^ fun stream ->
            receiveMessage defaultBufferSize WebSocketMessageType.Text stream socket
            ^-> fun _ -> stream |> IO.MemoryStream.ToUTF8String


[<AutoOpen>]
module ThreadSafeWebSocket =
    open System
    open Hopac
    open System.Net.WebSockets
    open Hopac.Infixes

    type SendMessage = WebSocket.BufferSize * WebSocketMessageType * IO.Stream  * IVar<unit> * Promise<unit>
    type ReceiveMessage=WebSocket.BufferSize * WebSocketMessageType * IO.Stream   * IVar<unit> * Promise<unit>
    type CloseMessage = WebSocketCloseStatus * string * IVar<unit> * Promise<unit>
    type CloseOutputMessage = WebSocketCloseStatus * string * IVar<unit> * Promise<unit>

    type ThreadSafeWebSocket =
        { websocket : WebSocket
          sendCh : Ch<SendMessage>
          receiveCh : Ch<ReceiveMessage>
          closeCh : Ch<CloseMessage>
          closeOutputCh : Ch<CloseOutputMessage> }
        interface IDisposable with
            member x.Dispose() =
                x.websocket.Dispose()
        member x.State =
            x.websocket.State
        member x.CloseStatus =
            x.websocket.CloseStatus |> Option.ofNullable
        member x.CloseStatusDescription =
            x.websocket.CloseStatusDescription

    /// Websockets only allow for one receive and one send at a time. This results in if multiple threads try to write to a stream, it will throw a `System.InvalidOperationException`. This wraps a websocket in a hopac server-client model that allows for multiple threads to write or read at the same time.
    /// See https://docs.microsoft.com/en-us/dotnet/api/system.net.websockets.websocket.receiveasync?view=netcore-2.0#Remarks
    module ThreadSafeWebSocket =
        /// Creates a threadsafe websocket from already created websocket.
        /// Websockets only allow for one receive and one send at a time. This results in if multiple threads try to write to a stream, it will throw a `System.InvalidOperationException`. This wraps a websocket in a hopac server-client model that allows for multiple threads to write or read at the same time.
        /// See https://docs.microsoft.com/en-us/dotnet/api/system.net.websockets.websocket.receiveasync?view=netcore-2.0#Remarks
        let createFromWebSocket (webSocket : WebSocket) =

            let self = {
                websocket = webSocket
                sendCh = Ch ()
                receiveCh = Ch ()
                closeCh = Ch ()
                closeOutputCh = Ch ()
            }

            let send () =
                self.sendCh ^=> fun (bufferSize, messageType, stream, reply, nack) -> job {
                    do! Alt.tryIn
                            (WebSocket.sendMessage bufferSize messageType stream webSocket)
                            (IVar.fill reply)
                            (IVar.fillFailure reply)
                        <|> nack
                }

            let receive () =
                self.receiveCh ^=> fun (bufferSize, messageType, stream, reply, nack) -> job {
                    do! Alt.tryIn
                            (WebSocket.receiveMessage bufferSize messageType stream webSocket)
                            (IVar.fill reply)
                            (IVar.fillFailure reply)
                        <|> nack
                }

            let close () =
                self.closeCh ^=> fun (status, message, reply, nack) -> job {
                    do! Alt.tryIn
                            (WebSocket.close status message webSocket)
                            (IVar.fill reply)
                            (IVar.fillFailure reply)
                        <|> nack
                }

            let closeOutput () =
                self.closeOutputCh ^=> fun (status, message, reply, nack) -> job {
                    do! Alt.tryIn
                            (WebSocket.closeOutput status message webSocket)
                            (IVar.fill reply)
                            (IVar.fillFailure reply)
                        <|> nack
                }

            let receiveProc = Job.delay ^ fun () ->
                receive ()
            let sendProc = Job.delay ^ fun () ->
                send () <|> close () <|> closeOutput ()

            Job.foreverServer sendProc
            >>=. Job.foreverServer receiveProc
            >>-. self


        /// Sends a whole message to the websocket read from the given stream
        let sendMessage wsts bufferSize messageType stream =
            wsts.sendCh *<-->- fun reply nack ->
                (bufferSize, messageType, stream, reply,nack)

        /// Sends the UTF8 string as a whole websocket message
        let sendMessageAsUTF8(wsts : ThreadSafeWebSocket) (text : string) =
            Alt.using
                (IO.MemoryStream.UTF8toMemoryStream text)
                ^ fun ms ->  sendMessage wsts  WebSocket.defaultBufferSize  WebSocketMessageType.Text ms

        /// Receives a whole message written to the given stream
        /// Attempts to handle closes gracefully
        let receiveMessage wsts bufferSize messageType stream =
            wsts.receiveCh *<-->- fun reply nack ->
                (bufferSize, messageType, stream, reply,nack)

        /// Receives a whole message as a utf8 string
        let receiveMessageAsUTF8 (wsts : ThreadSafeWebSocket)  =
            Alt.using
                (new IO.MemoryStream())
                ^ fun stream ->
                    receiveMessage wsts WebSocket.defaultBufferSize WebSocketMessageType.Text stream
                    ^-> fun () ->
                        stream |> IO.MemoryStream.ToUTF8String //Remove null terminator

        /// Grace approach to shutting down. Use when you want to other end to acknowledge the close.
        /// CloseAsync: https://docs.microsoft.com/en-us/dotnet/api/system.net.websockets.websocket.closeasync?view=netcore-2.0
        let close wsts status message =
            wsts.closeCh *<-->- fun reply nack ->
                (status, message, reply, nack)


        /// Hard approach to shutting down. Useful when you don't want the other end to acknowledge the close or this end has received a close notification and want to acknowledge the close.
        /// Alt: https://hopac.github.io/Hopac/Hopac.html#def:type%20Hopac.Alt
        /// CloseOutputAsync: https://docs.microsoft.com/en-us/dotnet/api/system.net.websockets.websocket.closeoutputasync?view=netcore-2.0
        let closeOutput wsts status message =
            wsts.closeOutputCh *<-->- fun reply nack ->
                (status, message, reply, nack)
