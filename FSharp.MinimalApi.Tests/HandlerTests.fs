module HandlerTests

open System
open System.Collections.Concurrent
open System.Net
open System.Net.Http
open System.Threading
open System.Threading.Tasks
open Microsoft.AspNetCore.Builder
open Microsoft.AspNetCore.Http
open Microsoft.AspNetCore.Mvc
open Microsoft.AspNetCore.Routing
open Microsoft.Extensions.DependencyInjection
open Xunit
open FSharp.MinimalApi.Builder
open Harness

/// Collects calls from handlers that return nothing, scoped to one app.
type Calls() =
  let calls = ConcurrentQueue<string>()
  member _.Add(call: string) = calls.Enqueue call
  member _.All = List.ofSeq calls

type Greeting = { Name: string; Punctuation: string }

[<NoComparison>]
type Bound =
  {
    [<FromRoute>]
    id: int
    [<FromQuery>]
    search: string
    [<FromHeader(Name = "X-Tenant")>]
    tenant: string
    [<FromBody>]
    greeting: Greeting
    [<FromServices>]
    calls: Calls
  }

let private withCalls (routes: EndpointsMap) =
  startWith (fun services -> services.AddSingleton<Calls>() |> ignore) (fun app -> routes.Apply app |> ignore)

let private callsOf (app: TestApp) =
  app.Services.GetRequiredService<Calls>().All

[<Fact>]
let ``unit handler returning a value`` () =
  task {
    use! app = serve (endpoints { get "/hello" (fun () -> "world") })
    let! response = get app "/hello"
    do! expectBody ok "world" response
  }

[<Fact>]
let ``unit handler returning unit responds with an empty 200`` () =
  task {
    use! app =
      withCalls (endpoints { post "/ping" (fun (req: {| calls: Calls |}) -> req.calls.Add "ping") })

    let! response = send app (request HttpMethod.Post "/ping")
    do! expectBody ok "" response
    Assert.Equal<string list>([ "ping" ], callsOf app)
  }

[<Fact>]
let ``unit handler with no parameters returning unit`` () =
  task {
    let calls = Calls()
    use! app = serve (endpoints { delete "/reset" (fun () -> calls.Add "reset") })
    let! response = delete app "/reset"
    do! expectBody ok "" response
    Assert.Equal<string list>([ "reset" ], calls.All)
  }

[<Fact>]
let ``anonymous record binds route and query values`` () =
  task {
    use! app =
      serve (
        endpoints {
          get "/inc/{v:int}" (fun (req: {| v: int; n: Nullable<int> |}) -> req.v + req.n.GetValueOrDefault 1)
        }
      )

    let! withQuery = get app "/inc/10?n=5"
    do! expectBody ok "15" withQuery
    let! withoutQuery = get app "/inc/10"
    do! expectBody ok "11" withoutQuery
  }

[<Fact>]
let ``record binds route, query, header, body and services`` () =
  task {
    use! app =
      withCalls (
        endpoints {
          put "/greet/{id}" (fun (req: Bound) ->
            req.calls.Add $"%d{req.id}"
            $"%s{req.tenant}:%s{req.search}:%s{req.greeting.Name}%s{req.greeting.Punctuation}")
        }
      )

    use message = request HttpMethod.Put "/greet/7?search=abc"
    message.Headers.Add("X-Tenant", "acme")
    message.Content <- Json.JsonContent.Create { Name = "Ada"; Punctuation = "!" }
    let! response = send app message
    do! expectBody ok "acme:abc:Ada!" response
    Assert.Equal<string list>([ "7" ], callsOf app)
  }

[<Fact>]
let ``handler returning a record serializes it as JSON`` () =
  task {
    use! app =
      serve (
        endpoints { get "/greeting/{name}" (fun (req: {| name: string |}) -> { Name = req.name; Punctuation = "?" }) }
      )

    let! response = get app "/greeting/Bob"
    let! body = expect ok response
    Assert.Equal("""{"name":"Bob","punctuation":"?"}""", body)
    Assert.Equal("application/json", (nonNull response.Content.Headers.ContentType).MediaType)
  }

[<Fact>]
let ``Task handlers with and without parameters`` () =
  task {
    let calls = Calls()

    use! app =
      serve (
        endpoints {
          get "/task" (fun () -> Task.FromResult "no params")
          get "/task/{v}" (fun (req: {| v: int |}) -> task { return req.v * 2 })
          post "/task" (fun () -> task { calls.Add "no params" })
          post "/task/{v}" (fun (req: {| v: int |}) -> task { calls.Add $"%d{req.v}" })
        }
      )

    let! noParams = get app "/task"
    do! expectBody ok "no params" noParams
    let! withParams = get app "/task/21"
    do! expectBody ok "42" withParams
    let! unitNoParams = send app (request HttpMethod.Post "/task")
    do! expectBody ok "" unitNoParams
    let! unitWithParams = send app (request HttpMethod.Post "/task/3")
    do! expectBody ok "" unitWithParams
    Assert.Equal<string list>([ "no params"; "3" ], calls.All)
  }

[<Fact>]
let ``Async handlers with and without parameters`` () =
  task {
    let calls = Calls()

    use! app =
      serve (
        endpoints {
          get "/async" (fun () -> async { return "no params" })
          get "/async/{v}" (fun (req: {| v: int |}) -> async { return req.v * 2 })
          put "/async" (fun () -> async { calls.Add "no params" })
          put "/async/{v}" (fun (req: {| v: int |}) -> async { calls.Add $"%d{req.v}" })
        }
      )

    let! noParams = get app "/async"
    do! expectBody ok "no params" noParams
    let! withParams = get app "/async/21"
    do! expectBody ok "42" withParams
    let! unitNoParams = send app (request HttpMethod.Put "/async")
    do! expectBody ok "" unitNoParams
    let! unitWithParams = send app (request HttpMethod.Put "/async/3")
    do! expectBody ok "" unitWithParams
    Assert.Equal<string list>([ "no params"; "3" ], calls.All)
  }

[<Fact>]
let ``Async handlers run with the request's cancellation token`` () =
  task {
    use! app =
      serve (
        endpoints {
          get "/token" (fun () ->
            async {
              let! token = Async.CancellationToken
              return token.CanBeCanceled
            })
        }
      )

    let! response = get app "/token"
    do! expectBody ok "true" response
  }

[<Fact>]
let ``Async handlers stop when the request is aborted`` () =
  task {
    let started =
      TaskCompletionSource<unit>(TaskCreationOptions.RunContinuationsAsynchronously)

    let stopped =
      TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously)

    use! app =
      serve (
        endpoints {
          get "/slow" (fun () ->
            async {
              use! _ = Async.OnCancel(fun () -> stopped.TrySetResult true |> ignore)
              started.SetResult(())
              do! Async.Sleep Timeout.Infinite
              return "never"
            })
        }
      )

    use abort = new CancellationTokenSource()
    let pending = app.Client.GetAsync("/slow", abort.Token)
    do! within started.Task
    abort.Cancel()
    let! cancelled = within stopped.Task
    Assert.True cancelled

    let! error =
      Assert.ThrowsAnyAsync<OperationCanceledException>(fun () -> pending :> Task)

    Assert.NotNull error
  }

[<Fact>]
let ``Delegate handlers are mapped as they are`` () =
  task {
    use! app =
      serve (
        endpoints {
          get "/delegate" (Func<string>(fun () -> "get"))
          post "/delegate/{v}" (Func<int, int>(fun v -> v + 1))
          put "/delegate" (Func<HttpContext, string>(fun ctx -> ctx.Request.Method))
          delete "/delegate" (Func<string>(fun () -> "delete"))
        }
      )

    let! getResponse = get app "/delegate"
    do! expectBody ok "get" getResponse
    let! postResponse = send app (request HttpMethod.Post "/delegate/1")
    do! expectBody ok "2" postResponse
    let! putResponse = send app (request HttpMethod.Put "/delegate")
    do! expectBody ok "PUT" putResponse
    let! deleteResponse = delete app "/delegate"
    do! expectBody ok "delete" deleteResponse
  }

[<Fact>]
let ``every verb maps only its own HTTP method`` () =
  task {
    use! app =
      serve (
        endpoints {
          get "/verb" (fun () -> "GET")
          post "/verb" (fun () -> "POST")
          put "/verb" (fun () -> "PUT")
          delete "/verb" (fun () -> "DELETE")
        }
      )

    for verb in [ HttpMethod.Get; HttpMethod.Post; HttpMethod.Put; HttpMethod.Delete ] do
      let! response = send app (request verb "/verb")
      do! expectBody ok verb.Method response

    let! patch = send app (request HttpMethod.Patch "/verb")
    let! _ = expect HttpStatusCode.MethodNotAllowed patch
    ()
  }

[<Fact>]
let ``the route handler builder can be configured per endpoint`` () =
  task {
    let named name (b: RouteHandlerBuilder) = b.WithName name

    use! app =
      serve (
        endpoints {
          get "/configured" (fun () -> "get") (named "get-it")
          post "/configured" (fun () -> "post") (named "post-it")
          put "/configured" (fun () -> "put") (named "put-it")
          delete "/configured" (fun () -> "delete") (named "delete-it")
          get "/configured-delegate" (Func<string>(fun () -> "d")) (named "get-delegate")
          post "/configured-delegate" (Func<string>(fun () -> "d")) (named "post-delegate")
          put "/configured-delegate" (Func<string>(fun () -> "d")) (named "put-delegate")
          delete "/configured-delegate" (Func<string>(fun () -> "d")) (named "delete-delegate")
        }
      )

    let nameOf verb route =
      (nonNull ((endpoint app verb route).Metadata.GetMetadata<IEndpointNameMetadata>())).EndpointName

    Assert.Equal("get-it", nameOf "GET" "/configured")
    Assert.Equal("post-it", nameOf "POST" "/configured")
    Assert.Equal("put-it", nameOf "PUT" "/configured")
    Assert.Equal("delete-it", nameOf "DELETE" "/configured")
    Assert.Equal("get-delegate", nameOf "GET" "/configured-delegate")
    Assert.Equal("post-delegate", nameOf "POST" "/configured-delegate")
    Assert.Equal("put-delegate", nameOf "PUT" "/configured-delegate")
    Assert.Equal("delete-delegate", nameOf "DELETE" "/configured-delegate")
  }

[<Fact>]
let ``non-generic Task handlers`` () =
  task {
    let calls = Calls()

    use! app =
      serve (
        endpoints {
          post "/plain" (fun () ->
            calls.Add "no params"
            Task.CompletedTask)

          post "/plain/{v}" (fun (req: {| v: int |}) ->
            calls.Add $"%d{req.v}"
            Task.CompletedTask)
        }
      )

    let! noParams = send app (request HttpMethod.Post "/plain")
    do! expectBody ok "" noParams
    let! withParams = send app (request HttpMethod.Post "/plain/3")
    do! expectBody ok "" withParams
    Assert.Equal<string list>([ "no params"; "3" ], calls.All)
  }

[<Fact>]
let ``handlers returning other generic types serialize them`` () =
  task {
    use! app =
      serve (endpoints { get "/list/{n}" (fun (req: {| n: int |}) -> [ 1 .. req.n ]) })

    let! response = get app "/list/3"
    do! expectBody ok "[1,2,3]" response
  }