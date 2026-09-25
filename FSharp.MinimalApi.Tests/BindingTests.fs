module BindingTests

open System
open System.Net
open System.Net.Http
open System.Reflection
open System.Text
open System.Threading.Tasks
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
    tenant: string | null
  }

type ItemId =
  | ItemId of int

  static member TryParse(value: string, result: byref<ItemId>) =
    match Int32.TryParse value with
    | true, n when n > 0 ->
      result <- ItemId n
      true
    | _ ->
      result <- Unchecked.defaultof<ItemId>
      false

type HeaderBound =
  | HeaderBound of string

  static member BindAsync(context: HttpContext, _parameter: ParameterInfo) =
    if context.Request.Headers.ContainsKey("X-Item") then
      ValueTask<HeaderBound>(HeaderBound(context.Request.Headers["X-Item"].ToString()))
    else
      ValueTask<HeaderBound>(Unchecked.defaultof<HeaderBound>)

type HeaderId =
  {
    [<FromHeader(Name = "X-Id")>]
    itemId: ItemId
  }

let private customHandler
  (req:
    {|
      itemId: ItemId
      headerBound: HeaderBound
    |})
  =
  let (ItemId id) = req.itemId
  let (HeaderBound header) = req.headerBound
  sprintf "%d:%s" id header

let private routes =
  endpoints {
    get "/items/{id:int}" (fun (req: {| id: int |}) -> req.id)
    get "/search" (fun (req: {| page: int; size: Nullable<int> |}) -> req.page * req.size.GetValueOrDefault 10)
    get "/tenant" (fun (req: Tenant) -> req.tenant)

    get "/optional-tenant" (fun (req: OptionalTenant) ->
      match req.tenant with
      | null -> "none"
      | tenant -> tenant)

    get "/custom/{itemId}" customHandler
    get "/custom-query" (fun (req: {| itemId: ItemId |}) -> let (ItemId id) = req.itemId in id)
    get "/custom-header" (fun (req: HeaderId) -> let (ItemId id) = req.itemId in id)

    post "/greetings" (fun (req: {| greeting: Greeting |}) -> req.greeting.Name)
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
let ``custom TryParse and BindAsync are used inside a named F# parameter shape`` () =
  task {
    use! app = serve routes
    use valid = request HttpMethod.Get "/custom/12"
    valid.Headers.Add("X-Item", "header")
    let! response = send app valid
    do! expectBody ok "12:header" response
    let! missingBound = get app "/custom/12"
    let! _ = expect HttpStatusCode.BadRequest missingBound
    let! invalid = get app "/custom/not-an-id"
    let! _ = expect HttpStatusCode.BadRequest invalid
    let! negative = get app "/custom/-1"
    let! _ = expect HttpStatusCode.BadRequest negative
    let! query = get app "/custom-query?itemId=13"
    do! expectBody ok "13" query
    let! badQuery = get app "/custom-query?itemId=bad"
    let! _ = expect HttpStatusCode.BadRequest badQuery
    use header = request HttpMethod.Get "/custom-header"
    header.Headers.Add("X-Id", "14")
    let! headerResult = send app header
    do! expectBody ok "14" headerResult
    let! missingHeader = get app "/custom-header"
    let! _ = expect HttpStatusCode.BadRequest missingHeader
    ()
  }

[<Fact>]
let ``option and voption are not inferred as query scalars`` () =
  task {
    let cases =
      [
        "/option",
        endpoints { get "/option" (fun (req: {| value: int option |}) -> req.value |> Option.defaultValue -1) }
        "/voption",
        endpoints { get "/voption" (fun (req: {| value: int voption |}) -> req.value |> ValueOption.defaultValue -1) }
      ]

    for url, route in cases do
      use! app = serve route

      let! error =
        Assert.ThrowsAsync<InvalidOperationException>(fun () -> get app (url + "?value=3") :> Task)

      Assert.Contains("Body was inferred", error.Message)
  }

[<Fact>]
let ``binding failures never run the named handler`` () =
  task {
    let calls = Calls()

    let handler (req: {| id: int; greeting: Greeting |}) =
      calls.Add(sprintf "%d" req.id)
      req.greeting.Name

    use! app = serve (endpoints { post "/probe/{id}" handler })
    let! badId = postRaw app "/probe/not-an-int" """{"name":"Ada","punctuation":"!"}"""
    let! _ = expect HttpStatusCode.BadRequest badId
    let! badBody = postRaw app "/probe/1" "{ broken"
    let! _ = expect HttpStatusCode.BadRequest badBody
    use wrongMedia = request HttpMethod.Post "/probe/1"
    wrongMedia.Content <- new StringContent("""{"name":"Ada"}""", Encoding.UTF8, "text/plain")
    let! unsupported = send app wrongMedia
    let! _ = expect HttpStatusCode.UnsupportedMediaType unsupported
    Assert.Empty calls.All
    let! valid = postRaw app "/probe/2" """{"name":"Ada","punctuation":"!"}"""
    do! expectBody ok "Ada" valid
    Assert.Equal<string list>([ "2" ], calls.All)
  }

[<Fact>]
let ``a missing body for a non-nullable parameter is a bad request`` () =
  task {
    use! app = serve routes
    let! response = send app (request HttpMethod.Post "/greetings")
    let! _ = expect HttpStatusCode.BadRequest response
    ()
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