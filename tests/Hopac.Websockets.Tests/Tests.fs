module Tests


open Hopac.Websockets
open Expecto
open Hopac
open System
open System.Net
open System.Net.WebSockets
open System.Threading
open System.Threading.Tasks
open Microsoft.AspNetCore.Builder
open Microsoft.AspNetCore.Hosting
open Microsoft.AspNetCore.Http
open Microsoft.AspNetCore.TestHost
open Microsoft.Extensions.Configuration
open Hopac.Websockets
open Expecto.Logging


[<AutoOpen>]
module Expecto =
    let testCaseJob name (job : Job<unit>) = testCaseAsync name (job |> Job.toAsync)

module Expect =
    let exceptionEquals exType message (ex : exn) =
        let actualExType = ex.GetType().ToString()
        if exType <> actualExType then
            Tests.failtestf "Expected exception of %s but got %s" exType actualExType
        if ex.Message <> message then
            Tests.failtestf "Expected message %s but got %s" message ex.Message

    let exceptionExists exType message (exns : exn seq) =
        let exnTypes =
            exns
            |> Seq.map ^ fun ex -> ex.GetType().ToString()
        Expect.contains exnTypes exType  "No exception matching that type found"

        let exnMessages =
            exns
            |> Seq.map ^ fun ex -> ex.Message
        Expect.contains exnMessages message  "No exception message matching that string found"

let random = Random(42)
let genStr =
    let chars = "ABCDEFGHIJKLMNOPQRSTUVWUXYZ0123456789"
    let charsLen = chars.Length


    fun len ->
        let randomChars = [|for _ in 0..len -> chars.[random.Next(charsLen)]|]
        new string(randomChars)

let echoWebSocket (httpContext : HttpContext) (next : unit -> Job<unit>) = job {
    if httpContext.WebSockets.IsWebSocketRequest then
        let! (websocket : WebSocket) = httpContext.WebSockets.AcceptWebSocketAsync()
        while websocket.State <> WebSocketState.Closed do
            do! websocket
                |> WebSocket.receiveMessageAsUTF8
                |> Job.bind(fun text -> WebSocket.sendMessageAsUTF8 text websocket)

        ()
    else
        do! next()
}

let juse (middlware : HttpContext -> (unit -> Job<unit>) -> Job<unit>) (app:IApplicationBuilder) =
    app.Use(
        Func<_,Func<_>,_>(
            fun env next ->
                middlware env (next.Invoke >> Job.awaitUnitTask)
                |> Hopac.startAsTask :> Task
))
let configureEchoServer (appBuilder : IApplicationBuilder) =
    appBuilder.UseWebSockets()
    |> juse (echoWebSocket)
    |> ignore

    ()

let getTestServer () =
     new TestServer(
            WebHostBuilder()
                .Configure(fun app -> configureEchoServer app))


let constructLocalUri port =
    sprintf "http://127.0.0.1:%d" port

let getKestrelServer uri = job {
    let configBuilder = new ConfigurationBuilder()
    let configBuilder = configBuilder.AddInMemoryCollection()
    let config = configBuilder.Build()
    config.["server.urls"] <- uri
    let host = WebHostBuilder()
                .UseConfiguration(config)
                .UseKestrel()
                .Configure(fun app -> configureEchoServer app )
                .Build()

    do! host.StartAsync() |> Job.awaitUnitTask
    return host
}

let getOpenClientWebSocket (testServer : TestServer) = job {
    let ws = testServer.CreateWebSocketClient()
    return! ws.ConnectAsync(testServer.BaseAddress, CancellationToken.None)
    // return ws
}

let getOpenWebSocket uri = job {
    let ws = new ClientWebSocket()
    do! ws.ConnectAsync(uri, CancellationToken.None) |> Job.awaitUnitTask
    return ws
}

// So we're able to tell the operating system to get a random free port by passing 0
// and the system usually doesn't reuse a port until it has to
// *pray*
let getPort () =
    let listener = new Sockets.TcpListener(IPAddress.Loopback,0)
    listener.Start()
    let port  = (listener.LocalEndpoint :?> IPEndPoint).Port
    listener.Stop()
    port

let inline getServerAndWs () = job {
    let uri = getPort () |> constructLocalUri
    // printfn "starting up %A" uri
    let builder = UriBuilder(uri)
    builder.Scheme <- "ws"
    let! server = getKestrelServer uri
    let! clientWebSocket = builder.Uri |> getOpenWebSocket
    return server, clientWebSocket
}

[<Tests>]
let tests =
    testList "samples" [
        yield!
            [1..10]
            |> Seq.map ^ fun index ->
                testCaseJob (sprintf "Echo Hello World - %d" index) <| job {
                    let! (server, clientWebSocket) = getServerAndWs()
                    use server = server
                    use clientWebSocket = clientWebSocket
                    let expected = genStr (2000 * index)
                    do! clientWebSocket  |> WebSocket.sendMessageAsUTF8 expected
                    let! actual = clientWebSocket |> WebSocket.receiveMessageAsUTF8
                    Expect.equal actual expected "did not echo"
                }

        yield
            testCaseJob "Many concurrent writes to websocket should throw exception" <| job {
                // To create thie exception we actually have to run against Kestrel and not TestHost
                // Go figure trying to get a timing exception to occur isn't always reliable
                // Job.catch returns empty exception sometimes so we'll keep trying until we get the exception we're looking for
                let rec inner (attempt : int) = job {
                    if attempt = 50 then
                        skiptest "Too many attempts. Skipping"
                    else

                        let! servers = getServerAndWs() |> Job.catch
                        match servers with
                        | Choice1Of2 (server, clientWebSocket) ->
                            use server = server
                            use clientWebSocket = clientWebSocket
                            let! result =
                                [1..(Environment.ProcessorCount + 5)]
                                |> Seq.map ^ fun _ ->
                                    clientWebSocket  |> WebSocket.sendMessageAsUTF8 (genStr 1000)
                                |> Job.conIgnore
                                |> Job.catch


                            match result with
                            | Choice2Of2 e ->
                                match e with
                                | :? AggregateException as ae ->
                                    let exns = ae.Flatten().InnerExceptions
                                    // exns
                                    // |> Expect.exceptionExists "System.Net.WebSockets.WebSocketException" "The WebSocket is in an invalid state ('Aborted') for this operation. Valid states are: 'Open, CloseReceived'"
                                    try
                                        exns
                                        |> Expect.exceptionExists "System.InvalidOperationException" "There is already one outstanding 'SendAsync' call for this WebSocket instance. ReceiveAsync and SendAsync can be called simultaneously, but at most one outstanding operation for each of them is allowed at the same time."
                                    with _ -> do! inner(attempt + 1)
                                | e -> do! inner (attempt + 1)
                            | _ ->
                                do! inner (attempt + 1)
                        | Choice2Of2 e ->
                            skiptest "Socket connection failed. Skipping"
                            // do! inner()
                }
                do! inner 0
            }

        yield
            testCaseJob "Many concurrent writes to ThreadSafeWebSocket shouldn't throw exception" <| job {
                    let! (server, clientWebSocket) = getServerAndWs()
                    use server = server
                    use clientWebSocket = clientWebSocket
                    let! threadSafeWebSocket = ThreadSafeWebSocket.createFromWebSocket clientWebSocket

                    let maxMessagesToSend = 500

                    let expected =
                        [1..maxMessagesToSend]
                        |> Seq.map ^ fun _ -> (genStr 100000)
                        |> Seq.toList
                    // let! sendResult =
                    expected
                        |> Seq.iter  (ThreadSafeWebSocket.sendMessageAsUTF8 threadSafeWebSocket >> start)
                        // |> Job.conIgnore
                        // |> Job.catch
                    // Expect.isChoice1Of2 sendResult "did not throw System.InvalidOperationException"
                    let! receiveResult =
                        [1..maxMessagesToSend]
                        |> Seq.map ^ fun _ ->
                            ThreadSafeWebSocket.readMessageAsUTF8 threadSafeWebSocket
                        |> Job.seqCollect
                    Expect.sequenceEqual (receiveResult |> Seq.sort) (expected |> Seq.sort) "Didn't echo properly"

                    do!  ThreadSafeWebSocket.close threadSafeWebSocket WebSocketCloseStatus.NormalClosure "End Test"
            }
  ]
