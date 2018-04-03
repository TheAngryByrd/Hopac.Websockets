namespace Hopac.Websockets.AspNetCore

[<AutoOpen>]
module Library =
    open Hopac
    open Hopac.Infixes
    open Hopac.Websockets
    type Microsoft.AspNetCore.Http.WebSocketManager with
        /// Transitions the request to a ThreadSafeWebSocket connection
        member this.AcceptThreadSafeWebsocket() =
            Job.fromTask(this.AcceptWebSocketAsync)
             >>= ThreadSafeWebSocket.createFromWebSocket

        /// Transitions the request to a ThreadSafeWebSocket connection using the specified sub-protocol.
        member this.AcceptThreadSafeWebsocket(subprotocol : string) =
            Job.fromTask(fun () -> this.AcceptWebSocketAsync subprotocol)
            >>= ThreadSafeWebSocket.createFromWebSocket

