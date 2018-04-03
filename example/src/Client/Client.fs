module Client

open Elmish
open Elmish.React

open Fable.Core
open Fable.Core.JsInterop
open Fable.Import
open Fable.Import.Browser
open Fable.Helpers.React
open Fable.Helpers.React.Props
open Fable.PowerPack.Fetch

open Shared



type Model = {
    Counter : Counter option
    Ticker : Ticker option }

type Msg =
| Increment
| Decrement
| InitCount of Result<int, exn>
| NewTick of Ticker



let init () =
  let model = {
      Counter = None
      Ticker = None
  }
  let cmd =
    Cmd.ofPromise
      (fetchAs<int> "/api/init")
      []
      (Ok >> InitCount)
      (Error >> InitCount)
  model, cmd

let update msg (model : Model) =
  let model' =
    match model.Counter,  msg with
    | Some x, Increment -> { model with Counter =  Some (x + 1) }
    | Some x, Decrement -> { model with Counter = Some (x - 1) }
    | None, InitCount (Ok x) ->
        { model with Counter = Some x }
    | _, NewTick tick -> { model with Ticker = Some tick }
    | _ -> { model with Counter = None }
  model', Cmd.none

let safeComponents =
  let intersperse sep ls =
    List.foldBack (fun x -> function
      | [] -> [x]
      | xs -> x::sep::xs) ls []

  let components =
    [
      "Giraffe", "https://github.com/giraffe-fsharp/Giraffe"
      "Fable", "http://fable.io"
      "Elmish", "https://fable-elmish.github.io/"
    ]
    |> List.map (fun (desc,link) -> a [ Href link ] [ str desc ] )
    |> intersperse (str ", ")
    |> span [ ]

  p [ ]
    [ strong [] [ str "SAFE Template" ]
      str " powered by: "
      components ]

let show = function
| Some x -> string x
| None -> "Loading..."

let showTick = function
| Some x -> string x
| None -> "Loading ticker..."

let view model dispatch =
  div []
    [ h1 [] [ str "SAFE Template" ]
      p  [] [ str "The initial counter is fetched from server" ]
      p  [] [ str "Press buttons to manipulate counter:" ]
      p [] [ str (showTick model.Ticker)]
      button [ OnClick (fun _ -> dispatch Decrement) ] [ str "-" ]
      div [] [ str (show model.Counter) ]
      button [ OnClick (fun _ -> dispatch Increment) ] [ str "+" ]
      safeComponents ]


let websocket = WebSocket.Create((sprintf "ws://%s/ws/tickerWS" window.location.host))

let webSocketSub initial =
    let sub dispatch =
        websocket.addEventListener_message(fun event ->
            unbox event.data |> NewTick |> dispatch
            null
        ) |> ignore
    Cmd.ofSub sub



#if DEBUG
open Elmish.Debug
open Elmish.HMR
#endif

Program.mkProgram init update view
|> Program.withSubscription webSocketSub
#if DEBUG
|> Program.withConsoleTrace
|> Program.withHMR
#endif
|> Program.withReact "elmish-app"
#if DEBUG
|> Program.withDebugger
#endif
|> Program.run
