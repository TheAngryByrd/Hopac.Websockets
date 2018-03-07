module Tests


open Expecto
open Hopac.Websockets

open Expecto
open BenchmarkDotNet

module Types =
  type Y = { a : string; b : int }

type Serialiser =
  abstract member Serialise<'a> : 'a -> unit

type MySlowSerialiser() =
  interface Serialiser with
    member x.Serialise _ =
      System.Threading.Thread.Sleep(30)

type FastSerialiser() =
  interface Serialiser with
    member x.Serialise _ =
      System.Threading.Thread.Sleep(10)

type FastSerialiserAlt() =
  interface Serialiser with
    member x.Serialise _ =
     System.Threading.Thread.Sleep(20)

type Serialisers() =
  let fast, fastAlt, slow =
    FastSerialiser() :> Serialiser,
    FastSerialiserAlt() :> Serialiser,
    MySlowSerialiser() :> Serialiser

  [<Benchmark>]
  member x.FastSerialiserAlt() = fastAlt.Serialise "Hello world"

  [<Benchmark>]
  member x.SlowSerialiser() = slow.Serialise "Hello world"

  [<Benchmark(Baseline = true)>]
  member x.FastSerialiser() = fast.Serialise "Hello world"

open Types

// let benchmark<'typ> config onSummary =
//     BenchmarkDotNet.Running.BenchmarkRunner.Run<'typ>(config) |> onSummary

[<Tests>]
let tests =
  testList "performance tests" [
    test "three serialisers" {
      benchmark<Serialisers> benchmarkConfig (fun _ -> obj()) |> ignore
    }
  ]

// [<Tests>]
// let tests =
//   testList "samples" [
//     testCase "Say hello all" <| fun _ ->
//       let subject = Say.hello "all"
//       Expect.equal subject "Hello all" "You didn't say hello"
//   ]
