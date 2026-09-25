module BindingTests

open System
open System.Net
open System.Net.Http
open System.Text
open Microsoft.AspNetCore.Http
open Microsoft.AspNetCore.Mvc
open Xunit
open FSharp.MinimalApi.Builder
open Harness
open HandlerTests

type Tenant =
  {
    [<FromHeader(Name = "X-Tenant")>]
    tenant: int
  }

type OptionalTenant =
  {
    [<FromHeader(Name = "X-Tenant")>]
    tenant: string
  }

let private routes =
  endpoints {
    get "/items/{id:int}" (fun (req: {| id: int |}) -> req.id)
    get "/search" (fun (req: {| page: int; size: Nullable<int> |}) -> req.page * req.size.GetValueOrDefault 10)
    get "/tenant" (fun (req: Tenant) -> req.tenant)
    get "/optional-tenant" (fun (req: OptionalTenant) -> if isNull req.tenant then "none" else req.tenant)

    post "/greetings" (fun (req: {| greeting: Greeting |}) ->
      if box req.greeting |> isNull then
        "none"
      else
        req.greeting.Name)
  }

let private postRaw (app: TestApp) (url: string) (json: string) =
  let message = request HttpMethod.Post url
  message.Content <- new StringContent(json, Encoding.UTF8, "application/json")
  send app message

[<Fact>]
let ``a route constraint that does not match is not found`` () =
  task {
    use! app = serve routes
    let! response = get app "/items/abc"
    let! _ = expect HttpStatusCode.NotFound response
    let! matched = get app "/items/12"
    do! expectBody ok "12" matched
  }

[<Fact>]
let ``a missing required query value is a bad request`` () =
  task {
    use! app = serve routes
    let! response = get app "/search"
    let! _ = expect HttpStatusCode.BadRequest response
    let! valid = get app "/search?page=2"
    do! expectBody ok "20" valid
  }

[<Fact>]
let ``a query value of the wrong type is a bad request`` () =
  task {
    use! app = serve routes
    let! wrongPage = get app "/search?page=two"
    let! _ = expect HttpStatusCode.BadRequest wrongPage
    let! wrongSize = get app "/search?page=1&size=big"
    let! _ = expect HttpStatusCode.BadRequest wrongSize
    ()
  }

[<Fact>]
let ``a missing required header is a bad request`` () =
  task {
    use! app = serve routes
    let! response = get app "/tenant"
    let! _ = expect HttpStatusCode.BadRequest response
    use message = request HttpMethod.Get "/tenant"
    message.Headers.Add("X-Tenant", "42")
    let! valid = send app message
    do! expectBody ok "42" valid
  }

// F# doesn't mark reference types as non-nullable, so ASP.NET treats them as optional.
[<Fact>]
let ``a missing header of a reference type binds as null`` () =
  task {
    use! app = serve routes
    let! response = get app "/optional-tenant"
    do! expectBody ok "none" response
  }

[<Fact>]
let ``a missing body binds as null`` () =
  task {
    use! app = serve routes
    let! response = send app (request HttpMethod.Post "/greetings")
    do! expectBody ok "none" response
  }

[<Fact>]
let ``malformed JSON is a bad request`` () =
  task {
    use! app = serve routes
    let! response = postRaw app "/greetings" "{ not json"
    let! _ = expect HttpStatusCode.BadRequest response
    let! valid = postRaw app "/greetings" """{"name":"Ada","punctuation":"."}"""
    do! expectBody ok "Ada" valid
  }

[<Fact>]
let ``a body of the wrong media type is unsupported`` () =
  task {
    use! app = serve routes
    let message = request HttpMethod.Post "/greetings"
    message.Content <- new StringContent("""{"name":"Ada"}""", Encoding.UTF8, "text/plain")
    let! response = send app message
    let! _ = expect HttpStatusCode.UnsupportedMediaType response
    ()
  }