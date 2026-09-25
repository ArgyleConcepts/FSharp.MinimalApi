module FilterTests

open System.Net
open System.Threading.Tasks
open Microsoft.AspNetCore.Http
open Microsoft.Extensions.DependencyInjection
open Xunit
open FSharp.MinimalApi.Builder
open Harness
open HandlerTests

/// Short-circuits with 418 when the query has ?teapot, otherwise lets the handler run.
type TeapotFilter() =
  interface IEndpointFilter with
    member _.InvokeAsync(ctx, next) =
      if ctx.HttpContext.Request.Query.ContainsKey "teapot" then
        ValueTask<obj>(Results.StatusCode 418)
      else
        next.Invoke ctx

/// Created by the container for every request, so it can take services.
type RecordingFilter(calls: Calls) =
  interface IEndpointFilter with
    member _.InvokeAsync(ctx, next) =
      calls.Add $"before {ctx.HttpContext.Request.Path}"
      next.Invoke ctx

let private hello = fun () -> "hello"

let private upper =
  fun (ctx: EndpointFilterInvocationContext) (next: EndpointFilterInvocationContext -> ValueTask<obj>) ->
    task {
      let! result = next ctx
      return box ((string result).ToUpperInvariant())
    }

[<Fact>]
let ``filter with a function returning ValueTask`` () =
  task {
    use! app =
      serve (
        endpoints {
          filter (fun ctx next ->
            if ctx.HttpContext.Request.Query.ContainsKey "stop" then
              ValueTask<obj>(Results.NoContent())
            else
              next ctx)

          get "/hello" hello
        }
      )

    let! passed = get app "/hello"
    do! expectBody ok "hello" passed
    let! stopped = get app "/hello?stop"
    do! expectBody HttpStatusCode.NoContent "" stopped
  }

[<Fact>]
let ``filter with a function returning Task can change the result`` () =
  task {
    use! app =
      serve (
        endpoints {
          filter upper
          get "/hello" hello
        }
      )

    let! response = get app "/hello"
    do! expectBody ok "HELLO" response
  }

[<Fact>]
let ``filter with an instance`` () =
  task {
    use! app =
      serve (
        endpoints {
          filter (TeapotFilter())
          get "/hello" hello
        }
      )

    let! passed = get app "/hello"
    do! expectBody ok "hello" passed
    let! stopped = get app "/hello?teapot"
    let! _ = expect (enum<HttpStatusCode> 418) stopped
    ()
  }

[<Fact>]
let ``filter with a constructor is created with services`` () =
  task {
    use! app =
      startWith (fun services -> services.AddSingleton<Calls>() |> ignore) (fun app ->
        (endpoints {
          route "recorded" {
            filter RecordingFilter
            get "/a" hello
            get "/b" hello
          }

          get "/plain" hello
        })
          .Apply
          app
        |> ignore)

    for url in [ "/recorded/a"; "/plain"; "/recorded/b" ] do
      let! response = get app url
      do! expectBody ok "hello" response

    Assert.Equal<string list>(
      [ "before /recorded/a"; "before /recorded/b" ],
      app.Services.GetRequiredService<Calls>().All
    )
  }

[<Fact>]
let ``filters see the bound arguments`` () =
  task {
    use! app =
      serve (
        endpoints {
          filter (fun ctx next ->
            match ctx.Arguments[0] with
            | :? {| id: int |} as req when req.id < 0 -> ValueTask<obj>(Results.BadRequest "negative")
            | _ -> next ctx)

          get "/items/{id}" (fun (req: {| id: int |}) -> req.id)
        }
      )

    let! valid = get app "/items/3"
    do! expectBody ok "3" valid
    let! invalid = get app "/items/-3"
    do! expectBody HttpStatusCode.BadRequest "\"negative\"" invalid
  }

[<Fact>]
let ``outer filters run before inner filters`` () =
  task {
    let order = Calls()

    let mark name =
      fun (ctx: EndpointFilterInvocationContext) (next: EndpointFilterInvocationContext -> ValueTask<obj>) ->
        order.Add name
        next ctx

    use! app =
      serve (
        endpoints {
          filter (mark "outer")

          route "inner" {
            filter (mark "inner")
            get "/x" hello
          }
        }
      )

    let! response = get app "/inner/x"
    do! expectBody ok "hello" response
    Assert.Equal<string list>([ "outer"; "inner" ], order.All)
  }