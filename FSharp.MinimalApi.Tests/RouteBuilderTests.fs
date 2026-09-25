module RouteBuilderTests

open System
open System.Net.Http
open Microsoft.AspNetCore.Builder
open Microsoft.AspNetCore.Routing
open Xunit
open FSharp.MinimalApi
open Harness

// The IEndpointRouteBuilder overloads take F# lambdas of up to 16 arguments without wrapping them in Func<>.
// Every argument binds from the query string, and the handler answers their sum.

let private names = "abcdefghijklmnop"

let private query arity =
  [ for i in 1..arity -> $"{names[i - 1]}={i}" ] |> String.concat "&"

let private sum arity = arity * (arity + 1) / 2

let private mapGet (app: IEndpointRouteBuilder) =
  app.MapGet("/1", (fun (a: int) -> a)) |> ignore
  app.MapGet("/2", (fun (a: int) (b: int) -> a + b)) |> ignore
  app.MapGet("/3", (fun (a: int) (b: int) (c: int) -> a + b + c)) |> ignore

  app.MapGet("/4", (fun (a: int) (b: int) (c: int) (d: int) -> a + b + c + d))
  |> ignore

  app.MapGet("/5", (fun (a: int) (b: int) (c: int) (d: int) (e: int) -> a + b + c + d + e))
  |> ignore

  app.MapGet("/6", (fun (a: int) (b: int) (c: int) (d: int) (e: int) (f: int) -> a + b + c + d + e + f))
  |> ignore

  app.MapGet("/7", (fun (a: int) (b: int) (c: int) (d: int) (e: int) (f: int) (g: int) -> a + b + c + d + e + f + g))
  |> ignore

  app.MapGet(
    "/8",
    fun (a: int) (b: int) (c: int) (d: int) (e: int) (f: int) (g: int) (h: int) -> a + b + c + d + e + f + g + h
  )
  |> ignore

  app.MapGet(
    "/9",
    fun (a: int) (b: int) (c: int) (d: int) (e: int) (f: int) (g: int) (h: int) (i: int) ->
      a + b + c + d + e + f + g + h + i
  )
  |> ignore

  app.MapGet(
    "/10",
    fun (a: int) (b: int) (c: int) (d: int) (e: int) (f: int) (g: int) (h: int) (i: int) (j: int) ->
      a + b + c + d + e + f + g + h + i + j
  )
  |> ignore

  app.MapGet(
    "/11",
    fun (a: int) (b: int) (c: int) (d: int) (e: int) (f: int) (g: int) (h: int) (i: int) (j: int) (k: int) ->
      a + b + c + d + e + f + g + h + i + j + k
  )
  |> ignore

  app.MapGet(
    "/12",
    fun (a: int) (b: int) (c: int) (d: int) (e: int) (f: int) (g: int) (h: int) (i: int) (j: int) (k: int) (l: int) ->
      a + b + c + d + e + f + g + h + i + j + k + l
  )
  |> ignore

  app.MapGet(
    "/13",
    fun
        (a: int)
        (b: int)
        (c: int)
        (d: int)
        (e: int)
        (f: int)
        (g: int)
        (h: int)
        (i: int)
        (j: int)
        (k: int)
        (l: int)
        (m: int) -> a + b + c + d + e + f + g + h + i + j + k + l + m
  )
  |> ignore

  app.MapGet(
    "/14",
    fun
        (a: int)
        (b: int)
        (c: int)
        (d: int)
        (e: int)
        (f: int)
        (g: int)
        (h: int)
        (i: int)
        (j: int)
        (k: int)
        (l: int)
        (m: int)
        (n: int) -> a + b + c + d + e + f + g + h + i + j + k + l + m + n
  )
  |> ignore

  app.MapGet(
    "/15",
    fun
        (a: int)
        (b: int)
        (c: int)
        (d: int)
        (e: int)
        (f: int)
        (g: int)
        (h: int)
        (i: int)
        (j: int)
        (k: int)
        (l: int)
        (m: int)
        (n: int)
        (o: int) -> a + b + c + d + e + f + g + h + i + j + k + l + m + n + o
  )
  |> ignore

  app.MapGet(
    "/16",
    fun
        (a: int)
        (b: int)
        (c: int)
        (d: int)
        (e: int)
        (f: int)
        (g: int)
        (h: int)
        (i: int)
        (j: int)
        (k: int)
        (l: int)
        (m: int)
        (n: int)
        (o: int)
        (p: int) -> a + b + c + d + e + f + g + h + i + j + k + l + m + n + o + p
  )
  |> ignore

let private mapPost (app: IEndpointRouteBuilder) =
  app.MapPost("/1", (fun (a: int) -> a)) |> ignore
  app.MapPost("/2", (fun (a: int) (b: int) -> a + b)) |> ignore
  app.MapPost("/3", (fun (a: int) (b: int) (c: int) -> a + b + c)) |> ignore

  app.MapPost("/4", (fun (a: int) (b: int) (c: int) (d: int) -> a + b + c + d))
  |> ignore

  app.MapPost("/5", (fun (a: int) (b: int) (c: int) (d: int) (e: int) -> a + b + c + d + e))
  |> ignore

  app.MapPost("/6", (fun (a: int) (b: int) (c: int) (d: int) (e: int) (f: int) -> a + b + c + d + e + f))
  |> ignore

  app.MapPost("/7", (fun (a: int) (b: int) (c: int) (d: int) (e: int) (f: int) (g: int) -> a + b + c + d + e + f + g))
  |> ignore

  app.MapPost(
    "/8",
    fun (a: int) (b: int) (c: int) (d: int) (e: int) (f: int) (g: int) (h: int) -> a + b + c + d + e + f + g + h
  )
  |> ignore

  app.MapPost(
    "/9",
    fun (a: int) (b: int) (c: int) (d: int) (e: int) (f: int) (g: int) (h: int) (i: int) ->
      a + b + c + d + e + f + g + h + i
  )
  |> ignore

  app.MapPost(
    "/10",
    fun (a: int) (b: int) (c: int) (d: int) (e: int) (f: int) (g: int) (h: int) (i: int) (j: int) ->
      a + b + c + d + e + f + g + h + i + j
  )
  |> ignore

  app.MapPost(
    "/11",
    fun (a: int) (b: int) (c: int) (d: int) (e: int) (f: int) (g: int) (h: int) (i: int) (j: int) (k: int) ->
      a + b + c + d + e + f + g + h + i + j + k
  )
  |> ignore

  app.MapPost(
    "/12",
    fun (a: int) (b: int) (c: int) (d: int) (e: int) (f: int) (g: int) (h: int) (i: int) (j: int) (k: int) (l: int) ->
      a + b + c + d + e + f + g + h + i + j + k + l
  )
  |> ignore

  app.MapPost(
    "/13",
    fun
        (a: int)
        (b: int)
        (c: int)
        (d: int)
        (e: int)
        (f: int)
        (g: int)
        (h: int)
        (i: int)
        (j: int)
        (k: int)
        (l: int)
        (m: int) -> a + b + c + d + e + f + g + h + i + j + k + l + m
  )
  |> ignore

  app.MapPost(
    "/14",
    fun
        (a: int)
        (b: int)
        (c: int)
        (d: int)
        (e: int)
        (f: int)
        (g: int)
        (h: int)
        (i: int)
        (j: int)
        (k: int)
        (l: int)
        (m: int)
        (n: int) -> a + b + c + d + e + f + g + h + i + j + k + l + m + n
  )
  |> ignore

  app.MapPost(
    "/15",
    fun
        (a: int)
        (b: int)
        (c: int)
        (d: int)
        (e: int)
        (f: int)
        (g: int)
        (h: int)
        (i: int)
        (j: int)
        (k: int)
        (l: int)
        (m: int)
        (n: int)
        (o: int) -> a + b + c + d + e + f + g + h + i + j + k + l + m + n + o
  )
  |> ignore

  app.MapPost(
    "/16",
    fun
        (a: int)
        (b: int)
        (c: int)
        (d: int)
        (e: int)
        (f: int)
        (g: int)
        (h: int)
        (i: int)
        (j: int)
        (k: int)
        (l: int)
        (m: int)
        (n: int)
        (o: int)
        (p: int) -> a + b + c + d + e + f + g + h + i + j + k + l + m + n + o + p
  )
  |> ignore

let private mapPut (app: IEndpointRouteBuilder) =
  app.MapPut("/1", (fun (a: int) -> a)) |> ignore
  app.MapPut("/2", (fun (a: int) (b: int) -> a + b)) |> ignore
  app.MapPut("/3", (fun (a: int) (b: int) (c: int) -> a + b + c)) |> ignore

  app.MapPut("/4", (fun (a: int) (b: int) (c: int) (d: int) -> a + b + c + d))
  |> ignore

  app.MapPut("/5", (fun (a: int) (b: int) (c: int) (d: int) (e: int) -> a + b + c + d + e))
  |> ignore

  app.MapPut("/6", (fun (a: int) (b: int) (c: int) (d: int) (e: int) (f: int) -> a + b + c + d + e + f))
  |> ignore

  app.MapPut("/7", (fun (a: int) (b: int) (c: int) (d: int) (e: int) (f: int) (g: int) -> a + b + c + d + e + f + g))
  |> ignore

  app.MapPut(
    "/8",
    fun (a: int) (b: int) (c: int) (d: int) (e: int) (f: int) (g: int) (h: int) -> a + b + c + d + e + f + g + h
  )
  |> ignore

  app.MapPut(
    "/9",
    fun (a: int) (b: int) (c: int) (d: int) (e: int) (f: int) (g: int) (h: int) (i: int) ->
      a + b + c + d + e + f + g + h + i
  )
  |> ignore

  app.MapPut(
    "/10",
    fun (a: int) (b: int) (c: int) (d: int) (e: int) (f: int) (g: int) (h: int) (i: int) (j: int) ->
      a + b + c + d + e + f + g + h + i + j
  )
  |> ignore

  app.MapPut(
    "/11",
    fun (a: int) (b: int) (c: int) (d: int) (e: int) (f: int) (g: int) (h: int) (i: int) (j: int) (k: int) ->
      a + b + c + d + e + f + g + h + i + j + k
  )
  |> ignore

  app.MapPut(
    "/12",
    fun (a: int) (b: int) (c: int) (d: int) (e: int) (f: int) (g: int) (h: int) (i: int) (j: int) (k: int) (l: int) ->
      a + b + c + d + e + f + g + h + i + j + k + l
  )
  |> ignore

  app.MapPut(
    "/13",
    fun
        (a: int)
        (b: int)
        (c: int)
        (d: int)
        (e: int)
        (f: int)
        (g: int)
        (h: int)
        (i: int)
        (j: int)
        (k: int)
        (l: int)
        (m: int) -> a + b + c + d + e + f + g + h + i + j + k + l + m
  )
  |> ignore

  app.MapPut(
    "/14",
    fun
        (a: int)
        (b: int)
        (c: int)
        (d: int)
        (e: int)
        (f: int)
        (g: int)
        (h: int)
        (i: int)
        (j: int)
        (k: int)
        (l: int)
        (m: int)
        (n: int) -> a + b + c + d + e + f + g + h + i + j + k + l + m + n
  )
  |> ignore

  app.MapPut(
    "/15",
    fun
        (a: int)
        (b: int)
        (c: int)
        (d: int)
        (e: int)
        (f: int)
        (g: int)
        (h: int)
        (i: int)
        (j: int)
        (k: int)
        (l: int)
        (m: int)
        (n: int)
        (o: int) -> a + b + c + d + e + f + g + h + i + j + k + l + m + n + o
  )
  |> ignore

  app.MapPut(
    "/16",
    fun
        (a: int)
        (b: int)
        (c: int)
        (d: int)
        (e: int)
        (f: int)
        (g: int)
        (h: int)
        (i: int)
        (j: int)
        (k: int)
        (l: int)
        (m: int)
        (n: int)
        (o: int)
        (p: int) -> a + b + c + d + e + f + g + h + i + j + k + l + m + n + o + p
  )
  |> ignore

let private mapDelete (app: IEndpointRouteBuilder) =
  app.MapDelete("/1", (fun (a: int) -> a)) |> ignore
  app.MapDelete("/2", (fun (a: int) (b: int) -> a + b)) |> ignore
  app.MapDelete("/3", (fun (a: int) (b: int) (c: int) -> a + b + c)) |> ignore

  app.MapDelete("/4", (fun (a: int) (b: int) (c: int) (d: int) -> a + b + c + d))
  |> ignore

  app.MapDelete("/5", (fun (a: int) (b: int) (c: int) (d: int) (e: int) -> a + b + c + d + e))
  |> ignore

  app.MapDelete("/6", (fun (a: int) (b: int) (c: int) (d: int) (e: int) (f: int) -> a + b + c + d + e + f))
  |> ignore

  app.MapDelete("/7", (fun (a: int) (b: int) (c: int) (d: int) (e: int) (f: int) (g: int) -> a + b + c + d + e + f + g))
  |> ignore

  app.MapDelete(
    "/8",
    fun (a: int) (b: int) (c: int) (d: int) (e: int) (f: int) (g: int) (h: int) -> a + b + c + d + e + f + g + h
  )
  |> ignore

  app.MapDelete(
    "/9",
    fun (a: int) (b: int) (c: int) (d: int) (e: int) (f: int) (g: int) (h: int) (i: int) ->
      a + b + c + d + e + f + g + h + i
  )
  |> ignore

  app.MapDelete(
    "/10",
    fun (a: int) (b: int) (c: int) (d: int) (e: int) (f: int) (g: int) (h: int) (i: int) (j: int) ->
      a + b + c + d + e + f + g + h + i + j
  )
  |> ignore

  app.MapDelete(
    "/11",
    fun (a: int) (b: int) (c: int) (d: int) (e: int) (f: int) (g: int) (h: int) (i: int) (j: int) (k: int) ->
      a + b + c + d + e + f + g + h + i + j + k
  )
  |> ignore

  app.MapDelete(
    "/12",
    fun (a: int) (b: int) (c: int) (d: int) (e: int) (f: int) (g: int) (h: int) (i: int) (j: int) (k: int) (l: int) ->
      a + b + c + d + e + f + g + h + i + j + k + l
  )
  |> ignore

  app.MapDelete(
    "/13",
    fun
        (a: int)
        (b: int)
        (c: int)
        (d: int)
        (e: int)
        (f: int)
        (g: int)
        (h: int)
        (i: int)
        (j: int)
        (k: int)
        (l: int)
        (m: int) -> a + b + c + d + e + f + g + h + i + j + k + l + m
  )
  |> ignore

  app.MapDelete(
    "/14",
    fun
        (a: int)
        (b: int)
        (c: int)
        (d: int)
        (e: int)
        (f: int)
        (g: int)
        (h: int)
        (i: int)
        (j: int)
        (k: int)
        (l: int)
        (m: int)
        (n: int) -> a + b + c + d + e + f + g + h + i + j + k + l + m + n
  )
  |> ignore

  app.MapDelete(
    "/15",
    fun
        (a: int)
        (b: int)
        (c: int)
        (d: int)
        (e: int)
        (f: int)
        (g: int)
        (h: int)
        (i: int)
        (j: int)
        (k: int)
        (l: int)
        (m: int)
        (n: int)
        (o: int) -> a + b + c + d + e + f + g + h + i + j + k + l + m + n + o
  )
  |> ignore

  app.MapDelete(
    "/16",
    fun
        (a: int)
        (b: int)
        (c: int)
        (d: int)
        (e: int)
        (f: int)
        (g: int)
        (h: int)
        (i: int)
        (j: int)
        (k: int)
        (l: int)
        (m: int)
        (n: int)
        (o: int)
        (p: int) -> a + b + c + d + e + f + g + h + i + j + k + l + m + n + o + p
  )
  |> ignore

let private verbs =
  dict [ "GET", mapGet; "POST", mapPost; "PUT", mapPut; "DELETE", mapDelete ]

let arities =
  TheoryData<string, int>(
    [
      for verb in verbs.Keys do
        for arity in 1..16 -> struct (verb, arity)
    ]
  )

[<Theory>]
[<MemberData(nameof arities)>]
let ``lambdas bind every argument`` (verb: string, arity: int) =
  task {
    use! app = start (fun app -> verbs[verb] app)
    let! response = send app (request (HttpMethod verb) $"/{arity}?{query arity}")
    do! expectBody ok $"{sum arity}" response
  }

[<Fact>]
let ``unit lambdas become parameterless handlers`` () =
  task {
    use! app =
      start (fun app ->
        app.MapGet("/", (fun () -> "GET")) |> ignore
        app.MapPost("/", (fun () -> "POST")) |> ignore
        app.MapPut("/", (fun () -> "PUT")) |> ignore
        app.MapDelete("/", (fun () -> "DELETE")) |> ignore)

    for verb in verbs.Keys do
      let! response = send app (request (HttpMethod verb) "/")
      do! expectBody ok verb response
  }

[<Fact>]
let ``lambdas return what the builder returns`` () =
  task {
    use! app =
      start (fun app -> app.MapGet("/named", (fun (id: int) -> id * 2)).WithName("double") |> ignore)

    let e = endpoint app "GET" "/named"
    Assert.Equal("double", e.Metadata.GetMetadata<IEndpointNameMetadata>().EndpointName)
    let! response = get app "/named?id=21"
    do! expectBody ok "42" response
  }

[<Fact>]
let ``Delegate.fromFuncWithMaybeUnit keeps other functions as they are`` () =
  let func = Func<int, int>(fun x -> x + 1)
  Assert.Same(func, Delegate.fromFuncWithMaybeUnit func)

[<Fact>]
let ``Delegate.fromFuncWithMaybeUnit drops a unit argument`` () =
  let handler = Delegate.fromFuncWithMaybeUnit (Func<unit, string>(fun () -> "unit"))
  Assert.Empty(handler.Method.GetParameters())
  Assert.Equal("unit", handler.DynamicInvoke() :?> string)