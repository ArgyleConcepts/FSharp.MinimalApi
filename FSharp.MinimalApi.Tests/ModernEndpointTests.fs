module ModernEndpointTests

open System
open System.Net
open System.Net.Http
open System.Text.Json.Nodes
open System.Threading.Tasks
open System.Threading.RateLimiting
open Microsoft.AspNetCore.Authorization
open Microsoft.AspNetCore.Builder
open Microsoft.AspNetCore.Http
open Microsoft.AspNetCore.Http.HttpResults
open Microsoft.AspNetCore.Http.Metadata
open Microsoft.AspNetCore.OutputCaching
open Microsoft.AspNetCore.RateLimiting
open Microsoft.AspNetCore.Routing
open Microsoft.Extensions.DependencyInjection
open Xunit
open FSharp.MinimalApi.Builder
open Harness
open type TypedResults

let private item (req: {| id: int |}) = Ok req.id

let private requiredMetadata<'T when 'T: not struct and 'T: not null> (mapped: Endpoint) =
  mapped.Metadata.GetMetadata<'T>() |> Option.ofObj |> Option.get

let private requiredProperty (node: JsonNode) (name: string) =
  node[name] |> Option.ofObj |> Option.get

[<Fact>]
let ``PATCH and methods map F# handlers inside groups`` () =
  task {
    use! app =
      startWith (fun services -> services.AddOpenApi() |> ignore) (fun app ->
        app.MapOpenApi() |> ignore

        let routes =
          endpoints {
            group "api"

            patch "/items/{id}" item (fun (b: RouteHandlerBuilder) ->
              b.WithName("patch-item").WithSummary("Patch an item").DisableValidation())

            methods "/inspect/{id}" [ "HEAD"; "OPTIONS" ] (fun (req: {| id: int; ctx: HttpContext |}) ->
              req.ctx.Response.Headers["X-Id"] <- string req.id
              NoContent())
          }

        routes.Apply app |> ignore)

    let patched = endpoint app "PATCH" "/api/items/{id}"
    Assert.Equal("patch-item", (requiredMetadata<IEndpointNameMetadata> patched).EndpointName)
    Assert.Equal("Patch an item", (requiredMetadata<IEndpointSummaryMetadata> patched).Summary)
    Assert.Equal<(int * Type) list>([ 200, typeof<int> ], producedBy patched)
    let! response = send app (request HttpMethod.Patch "/api/items/42")
    do! expectBody ok "42" response

    let mapped = endpoint app "HEAD" "/api/inspect/{id}"

    Assert.Equal<string list>(
      [ "HEAD"; "OPTIONS" ],
      (requiredMetadata<IHttpMethodMetadata> mapped).HttpMethods |> List.ofSeq
    )

    for verb in [ HttpMethod.Head; HttpMethod.Options ] do
      let! result = send app (request verb "/api/inspect/7")
      do! expectBody HttpStatusCode.NoContent "" result
      Assert.Equal("7", result.Headers.GetValues("X-Id") |> Seq.head)

    let! notMapped = get app "/api/items/42"
    let! _ = expect HttpStatusCode.MethodNotAllowed notMapped

    let! document =
      app.Client.GetStringAsync("/openapi/v1.json", TestContext.Current.CancellationToken)

    let json = JsonNode.Parse(document) |> Option.ofObj |> Option.get

    [ "paths"; "/api/items/{id}"; "patch"; "responses"; "200" ]
    |> List.fold requiredProperty json
    |> Assert.IsType<JsonObject>
    |> ignore
  }

[<Fact>]
let ``PATCH and methods keep typed results for sync Task and Async handlers`` () =
  task {
    use! app =
      serve (
        endpoints {
          patch "/patch/sync/{id}" produces<Ok<int>> item
          patch "/patch/task/{id}" produces<Ok<int>> (fun req -> task { return item req })
          patch "/patch/async/{id}" produces<Ok<int>> (fun req -> async { return item req })
          methods "/methods/sync/{id}" [ "REPORT" ] produces<Ok<int>> item
          methods "/methods/task/{id}" [ "SEARCH" ] produces<Ok<int>> (fun req -> task { return item req })
          methods "/methods/async/{id}" [ "PROPFIND" ] produces<Ok<int>> (fun req -> async { return item req })
        }
      )

    for verb, shape, prefix in
      [
        "PATCH", "sync", "patch"
        "PATCH", "task", "patch"
        "PATCH", "async", "patch"
        "REPORT", "sync", "methods"
        "SEARCH", "task", "methods"
        "PROPFIND", "async", "methods"
      ] do
      let route = sprintf "/%s/%s/{id}" prefix shape
      Assert.Equal<(int * Type) list>([ 200, typeof<int> ], producedBy (endpoint app verb route))

      let! response =
        send app (request (HttpMethod verb) (sprintf "/%s/%s/9" prefix shape))

      do! expectBody ok "9" response
  }

[<Fact>]
let ``PATCH and methods accept .NET delegates`` () =
  task {
    use! app =
      serve (
        endpoints {
          patch "/delegate" (Func<string>(fun () -> "patched"))
          methods "/delegate" [ "HEAD" ] (Func<IResult>(fun () -> NoContent()))
        }
      )

    let! patched = send app (request HttpMethod.Patch "/delegate")
    do! expectBody ok "patched" patched
    let mapped = endpoint app "HEAD" "/delegate"
    Assert.Equal<string list>([ "HEAD" ], (requiredMetadata<IHttpMethodMetadata> mapped).HttpMethods |> List.ofSeq)
    let! headed = send app (request HttpMethod.Head "/delegate")
    do! expectBody HttpStatusCode.NoContent "" headed
  }

[<Fact>]
let ``group policies and per-endpoint configuration retain framework metadata`` () =
  task {
    use! app =
      serve (
        endpoints {
          route "api" {
            summary "Group summary"
            requireAuthorization "members"
            rateLimit "one"
            outputCache "short"

            get "/item" (fun () -> "item") (fun (b: RouteHandlerBuilder) ->
              b.WithName("get-item").WithSummary("Get one item"))

            get "/other" (fun () -> "other")
          }
        }
      )

    let mapped = endpoint app "GET" "/api/item"
    Assert.Equal("get-item", (requiredMetadata<IEndpointNameMetadata> mapped).EndpointName)
    Assert.Equal("Get one item", (requiredMetadata<IEndpointSummaryMetadata> mapped).Summary)
    Assert.Contains(mapped.Metadata.GetOrderedMetadata<IAuthorizeData>(), fun auth -> auth.Policy = "members")
    Assert.Equal("one", (requiredMetadata<EnableRateLimitingAttribute> mapped).PolicyName)
    Assert.NotNull(mapped.Metadata.GetMetadata<IOutputCachePolicy>())
    let other = endpoint app "GET" "/api/other"
    Assert.Equal("Group summary", (requiredMetadata<IEndpointSummaryMetadata> other).Summary)
    Assert.Contains(other.Metadata.GetOrderedMetadata<IAuthorizeData>(), fun auth -> auth.Policy = "members")
    Assert.Equal("one", (requiredMetadata<EnableRateLimitingAttribute> other).PolicyName)
    Assert.NotNull(other.Metadata.GetMetadata<IOutputCachePolicy>())
  }

[<Fact>]
let ``group filter runs for PATCH without changing other routes`` () =
  task {
    use! app =
      serve (
        endpoints {
          route "filtered" {
            filter (fun ctx next ->
              if ctx.HttpContext.Request.Headers.ContainsKey("X-Block") then
                ValueTask<obj>(BadRequest() :> obj)
              else
                next ctx)

            patch "/item" (fun () -> "patched")
          }

          get "/plain" (fun () -> "plain")
        }
      )

    let! allowed = send app (request HttpMethod.Patch "/filtered/item")
    do! expectBody ok "patched" allowed
    use blockedRequest = request HttpMethod.Patch "/filtered/item"
    blockedRequest.Headers.Add("X-Block", "true")
    let! blocked = send app blockedRequest
    let! _ = expect HttpStatusCode.BadRequest blocked
    let! plain = get app "/plain"
    do! expectBody ok "plain" plain
  }

[<Fact>]
let ``group rate limit policy uses ASP.NET Core middleware`` () =
  task {
    use! app =
      startWith
        (fun services ->
          services.AddRateLimiter(fun options ->
            options.AddFixedWindowLimiter(
              "one",
              fun limiter ->
                limiter.PermitLimit <- 1
                limiter.Window <- TimeSpan.FromMinutes 1.
            )
            |> ignore

            options.RejectionStatusCode <- 429)
          |> ignore)
        (fun app ->
          app.UseRouting() |> ignore
          app.UseRateLimiter() |> ignore

          let routes =
            route "limited" {
              rateLimit "one"
              get "/item" (fun () -> "limited")
            }

          routes.Apply app |> ignore)

    let! first = get app "/limited/item"
    do! expectBody ok "limited" first
    let! second = get app "/limited/item"
    let! _ = expect HttpStatusCode.TooManyRequests second
    ()
  }

[<Fact>]
let ``group output cache policy uses ASP.NET Core middleware`` () =
  task {
    let mutable calls = 0

    use! app =
      startWith
        (fun services ->
          services.AddOutputCache(fun options ->
            options.AddPolicy("short", fun policy -> policy.Expire(TimeSpan.FromMinutes 1.) |> ignore))
          |> ignore)
        (fun app ->
          app.UseRouting() |> ignore
          app.UseOutputCache() |> ignore

          let routes =
            route "cached" {
              outputCache "short"

              get "/item" (fun () ->
                calls <- calls + 1
                string calls)
            }

          routes.Apply app |> ignore)

    let! first = get app "/cached/item"
    do! expectBody ok "1" first
    let! second = get app "/cached/item"
    do! expectBody ok "1" second
    Assert.Equal(1, calls)
  }