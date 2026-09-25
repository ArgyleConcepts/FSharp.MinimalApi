[<AutoOpen>]
module FSharp.MinimalApi.Builder.Builders

open System.Diagnostics.CodeAnalysis

// Only ever inlined at the call site, so their compiled bodies never run.
[<ExcludeFromCodeCoverage>]
let inline private implicit (x: ^a) : ^b =
    ((^a or ^b): (static member op_Implicit: ^a -> ^b) x)

[<ExcludeFromCodeCoverage>]
let inline (!!) v = implicit v

let endpoints = EndpointsBuilder()
let route groupName = EndpointsBuilder groupName
