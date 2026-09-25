module AuthTests

open System.Net
open System.Net.Http
open System.Security.Claims
open System.Threading.Tasks
open Microsoft.AspNetCore.Authentication
open Microsoft.AspNetCore.Authorization
open Microsoft.AspNetCore.Builder
open Microsoft.Extensions.DependencyInjection
open Xunit
open FSharp.MinimalApi.Builder
open Harness

[<Literal>]
let Scheme = "Header"

/// Authenticates the user named in X-User, with the roles listed in X-Roles.
type HeaderAuthentication(options, logger, encoder) =
  inherit AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)

  override this.HandleAuthenticateAsync() =
    match string this.Request.Headers["X-User"] with
    | "" -> Task.FromResult(AuthenticateResult.NoResult())
    | user ->
      let roles =
        (string this.Request.Headers["X-Roles"]).Split(',', System.StringSplitOptions.RemoveEmptyEntries)

      let claims =
        Claim(ClaimTypes.Name, user)
        :: [ for role in roles -> Claim(ClaimTypes.Role, role) ]

      let principal = ClaimsPrincipal(ClaimsIdentity(claims, Scheme))
      Task.FromResult(AuthenticateResult.Success(AuthenticationTicket(principal, Scheme)))

let private serveSecured (routes: EndpointsMap) =
  startWith
    (fun services ->
      services.AddAuthentication(Scheme).AddScheme<AuthenticationSchemeOptions, HeaderAuthentication>(Scheme, ignore)
      |> ignore

      services.AddAuthorization(fun options ->
        options.AddPolicy("Admins", (fun policy -> policy.RequireRole "admin" |> ignore)))
      |> ignore)
    (fun app ->
      app.UseAuthentication().UseAuthorization() |> ignore
      routes.Apply app |> ignore)

type User =
  | Anonymous
  | User
  | Admin

let private call (app: TestApp) (user: User) (url: string) =
  let message = request HttpMethod.Get url

  match user with
  | Anonymous -> ()
  | User -> message.Headers.Add("X-User", "ann")
  | Admin ->
    message.Headers.Add("X-User", "root")
    message.Headers.Add("X-Roles", "admin")

  send app message

let private expectStatus (app: TestApp) user url (status: HttpStatusCode) =
  task {
    let! response = call app user url
    let! _ = expect status response
    ()
  }

let private whoAmI =
  fun (req: {| user: ClaimsPrincipal |}) ->
    if isNull req.user.Identity.Name then
      "anonymous"
    else
      req.user.Identity.Name

[<Fact>]
let ``requireAuthorization requires an authenticated user`` () =
  task {
    use! app =
      serveSecured (
        endpoints {
          route "secure" {
            requireAuthorization
            get "/me" whoAmI
          }

          get "/open" whoAmI
        }
      )

    do! expectStatus app Anonymous "/secure/me" HttpStatusCode.Unauthorized
    let! response = call app User "/secure/me"
    do! expectBody ok "ann" response
    let! anonymous = call app Anonymous "/open"
    do! expectBody ok "anonymous" anonymous
  }

let private requiresAdmin (routes: EndpointsMap) =
  task {
    use! app = serveSecured routes
    do! expectStatus app Anonymous "/admin/me" HttpStatusCode.Unauthorized
    do! expectStatus app User "/admin/me" HttpStatusCode.Forbidden
    let! response = call app Admin "/admin/me"
    do! expectBody ok "root" response
  }

[<Fact>]
let ``requireAuthorization with policy names`` () =
  requiresAdmin (
    route "admin" {
      requireAuthorization "Admins"
      get "/me" whoAmI
    }
  )

[<Fact>]
let ``requireAuthorization with authorize data`` () =
  requiresAdmin (
    route "admin" {
      requireAuthorization (AuthorizeAttribute(Roles = "admin"))
      get "/me" whoAmI
    }
  )

[<Fact>]
let ``requireAuthorization with a policy`` () =
  requiresAdmin (
    route "admin" {
      requireAuthorization (AuthorizationPolicyBuilder().RequireRole("admin").Build())
      get "/me" whoAmI
    }
  )

[<Fact>]
let ``requireAuthorization with a policy builder`` () =
  requiresAdmin (
    route "admin" {
      requireAuthorization (fun (policy: AuthorizationPolicyBuilder) -> policy.RequireRole "admin" |> ignore)
      get "/me" whoAmI
    }
  )

[<Fact>]
let ``authorization applies to nested groups`` () =
  requiresAdmin (
    route "admin" {
      requireAuthorization "Admins"
      endpoints { get "/me" whoAmI }
    }
  )

[<Fact>]
let ``allowAnonymous opens a group inside a secured one`` () =
  task {
    use! app =
      serveSecured (
        route "api" {
          requireAuthorization "Admins"
          get "/secret" whoAmI

          route "public" {
            allowAnonymous
            get "/me" whoAmI
          }
        }
      )

    do! expectStatus app User "/api/secret" HttpStatusCode.Forbidden
    let! anonymous = call app Anonymous "/api/public/me"
    do! expectBody ok "anonymous" anonymous
    let! user = call app User "/api/public/me"
    do! expectBody ok "ann" user
  }