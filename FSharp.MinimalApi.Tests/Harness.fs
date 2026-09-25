module Harness

open System
open System.Net
open System.Net.Http
open System.Net.Http.Json
open System.Threading.Tasks
open Microsoft.AspNetCore.Builder
open Microsoft.AspNetCore.Hosting
open Microsoft.AspNetCore.Http
open Microsoft.AspNetCore.Routing
open Microsoft.AspNetCore.TestHost
open Microsoft.Extensions.DependencyInjection
open Microsoft.Extensions.Logging
open Xunit

/// A started in-memory app. Every test gets its own, so nothing is shared between tests.
type TestApp(app: WebApplication) =
  let client = app.GetTestClient()

  member _.Client = client
  member _.Services = app.Services

  /// Every endpoint the app mapped, including the ones added to route groups.
  member _.Endpoints =
    (app :> IEndpointRouteBuilder).DataSources
    |> Seq.collect _.Endpoints
    |> Seq.cast<RouteEndpoint>
    |> List.ofSeq

  interface IAsyncDisposable with
    member _.DisposeAsync() =
      client.Dispose()
      app.DisposeAsync()

let private cancellation () = TestContext.Current.CancellationToken

/// Waits for a signal from a handler, failing instead of hanging when it never comes.
let within (signal: Task<'a>) =
  signal.WaitAsync(TimeSpan.FromSeconds 10., cancellation ())

/// Builds and starts an app on the test server.
let startWith (services: IServiceCollection -> unit) (configure: WebApplication -> unit) =
  task {
    let builder = WebApplication.CreateSlimBuilder()
    builder.WebHost.UseTestServer() |> ignore
    builder.Logging.ClearProviders() |> ignore
    services builder.Services
    let app = builder.Build()
    configure app
    do! app.StartAsync(cancellation ())
    return new TestApp(app)
  }

let start configure = startWith ignore configure

/// Maps the routes at the application root.
let serve (routes: FSharp.MinimalApi.Builder.EndpointsMap) =
  start (fun app -> routes.Apply app |> ignore)

let send (app: TestApp) (request: HttpRequestMessage) =
  app.Client.SendAsync(request, cancellation ())

let request (httpMethod: HttpMethod) (url: string) = new HttpRequestMessage(httpMethod, url)

let get (app: TestApp) (url: string) = send app (request HttpMethod.Get url)

let delete (app: TestApp) (url: string) =
  send app (request HttpMethod.Delete url)

let postJson (app: TestApp) (url: string) (body: 'a) =
  app.Client.PostAsJsonAsync(url, body, cancellation ())

let putJson (app: TestApp) (url: string) (body: 'a) =
  app.Client.PutAsJsonAsync(url, body, cancellation ())

let bodyOf (response: HttpResponseMessage) =
  response.Content.ReadAsStringAsync(cancellation ())

let jsonOf<'a> (response: HttpResponseMessage) =
  response.Content.ReadFromJsonAsync<'a>(cancellation ())

/// Asserts the status code and returns the body.
let expect (status: HttpStatusCode) (response: HttpResponseMessage) =
  task {
    let! body = bodyOf response
    Assert.True((status = response.StatusCode), $"Expected {status} but got {response.StatusCode}: {body}")
    return body
  }

let expectBody (status: HttpStatusCode) (expected: string) (response: HttpResponseMessage) =
  task {
    let! body = expect status response
    Assert.Equal(expected, body)
  }

/// The endpoint mapped for the HTTP method at the route (as written after group prefixes are applied).
let endpoint (app: TestApp) (httpMethod: string) (route: string) =
  let matches (e: RouteEndpoint) =
    let methods = e.Metadata.GetMetadata<IHttpMethodMetadata>()

    ("/" + e.RoutePattern.RawText.TrimStart('/')) = route
    && not (isNull methods)
    && methods.HttpMethods |> Seq.contains httpMethod

  match app.Endpoints |> List.filter matches with
  | [ e ] -> e
  | found ->
    let all =
      app.Endpoints
      |> List.map (fun e -> $"{e.RoutePattern.RawText} {e.DisplayName}")
      |> String.concat "\n"

    failwith $"Expected one {httpMethod} {route} endpoint but found {found.Length}. Mapped:\n{all}"

/// Status code and body type of every response the endpoint declares.
let producedBy (e: Endpoint) =
  e.Metadata.GetOrderedMetadata<Microsoft.AspNetCore.Http.Metadata.IProducesResponseTypeMetadata>()
  |> Seq.map (fun m -> m.StatusCode, (if isNull m.Type then typeof<Void> else m.Type))
  |> Seq.distinct
  |> Seq.sortBy (fun (status, t) -> status, t.FullName)
  |> List.ofSeq

let tagsOf (e: Endpoint) =
  e.Metadata.GetOrderedMetadata<Microsoft.AspNetCore.Http.Metadata.ITagsMetadata>()
  |> Seq.collect _.Tags
  |> List.ofSeq

let descriptionOf (e: Endpoint) =
  e.Metadata.GetOrderedMetadata<Microsoft.AspNetCore.Http.Metadata.IEndpointDescriptionMetadata>()
  |> Seq.map _.Description
  |> List.ofSeq

let ok = HttpStatusCode.OK