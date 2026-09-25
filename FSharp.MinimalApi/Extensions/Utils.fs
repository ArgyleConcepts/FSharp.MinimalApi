namespace FSharp.MinimalApi

open System
open System.Threading.Tasks
open FSharp.Core

module Delegate =
    let fromFuncWithMaybeUnit (func: Func<'a, 'b>) : Delegate =
        match box func with
        | :? Func<unit, 'b> as f -> Func<'b>((f |> unbox<Func<unit, 'b>>).Invoke) :> Delegate
        | _ -> func :> Delegate

[<AutoOpen>]
module internal Utils =
    let tap f arg =
        f arg |> ignore
        arg
