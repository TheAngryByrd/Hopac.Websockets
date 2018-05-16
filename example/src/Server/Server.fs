open System
open System.IO
open System.Net.WebSockets
open System.Threading.Tasks

open Microsoft.AspNetCore
open Microsoft.AspNetCore.Builder
open Microsoft.AspNetCore.Hosting
open Microsoft.Extensions.DependencyInjection
open Newtonsoft.Json
open Giraffe
open Hopac
open Hopac.Websockets
open Hopac.Websockets.AspNetCore

open Shared


let clientPath = Path.Combine("..","Client") |> Path.GetFullPath
let port = 8085us

let getInitCounter () : Task<Counter> = task { return 42 }

type Dependencies = {
    TickerStream : Hopac.Stream<Ticker>
}

let webApp (dependencies: Dependencies ): HttpHandler =
    choose [
      route "/api/init" >=>
        fun next ctx ->
          task {
            let! counter = getInitCounter()
            return! Successful.OK counter next ctx
      }
      route "/ws/tickerWS" >=>
        fun next ctx ->
            task {
                if ctx.WebSockets.IsWebSocketRequest then
                    printfn "Received websocket request!"
                    let finished = IVar ()
                    job {
                        try
                            let! threadSafeWebSocket = ctx.WebSockets.AcceptThreadSafeWebsocket()
                            printfn "Connected websocket request!"
                            while threadSafeWebSocket.websocket.State <> WebSocketState.Closed do
                                do!
                                dependencies.TickerStream
                                |> Stream.mapFun JsonConvert.SerializeObject
                                |> Stream.iterJob (ThreadSafeWebSocket.sendMessageAsUTF8 threadSafeWebSocket)
                        with e ->
                            printfn "sendMessageAsUTF8 error %A" e
                        do! IVar.tryFill finished ()
                    } |> start
                    job {
                        try
                            let! threadSafeWebSocket = ctx.WebSockets.AcceptThreadSafeWebsocket()
                            printfn "Connected websocket request!"
                            while threadSafeWebSocket.websocket.State <> WebSocketState.Closed do
                                let! result = ThreadSafeWebSocket.receiveMessageAsUTF8 threadSafeWebSocket
                                printfn "received msg %s" result
                        with e ->
                            printfn "receiveMessageAsUTF8 error %A" e
                        do! IVar.tryFill finished ()
                    } |> start
                    do! finished |> startAsTask
                    return! Successful.ok (text "OK") next ctx
                else
                    return! next ctx
            }
    ]




let configureApp dependencies (app : IApplicationBuilder) =
  app.UseStaticFiles()
     .UseWebSockets()
     .UseGiraffe (webApp dependencies)



open Hopac.Stream

let ticker () =
    let src = Stream.Src.create()

    let looper index = job {
       do! timeOutMillis 1000
       let ticker = {
           Timestamp = DateTimeOffset.UtcNow
           Value = index
           Name = "Foo"
       }
       do! Stream.Src.value src ticker
       return index + 1
    }

    looper
    |> Job.iterateServer 0
    |> start

    Stream.Src.tap src


let configureServices (services : IServiceCollection) =
    services.AddGiraffe() |> ignore

let dependencies = {
    TickerStream = ticker ()
}

JsonConvert.DefaultSettings <- fun () ->
    let setttings = JsonSerializerSettings()
    setttings.Converters.Add(Fable.JsonConverter())
    setttings

WebHost
  .CreateDefaultBuilder()
  .UseWebRoot(clientPath)
  .UseContentRoot(clientPath)
  .Configure(Action<IApplicationBuilder> (configureApp dependencies))
  .ConfigureServices(configureServices)
  .UseUrls("http://0.0.0.0:" + port.ToString() + "/")
  .Build()
  .Run()
