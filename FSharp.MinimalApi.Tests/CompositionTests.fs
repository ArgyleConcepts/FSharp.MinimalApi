module CompositionTests

open System.Net
open Microsoft.AspNetCore.Builder
open Microsoft.AspNetCore.Http
open Microsoft.AspNetCore.Routing
open Xunit
open FSharp.MinimalApi.Builder
open Harness

let private routesOf (app: TestApp) =
  app.Endpoints |> List.map (fun e -> "/" + e.RoutePattern.RawText.TrimStart('/'))

let private answers (app: TestApp) (url: string) =
  task {
    let! response = get app url
    do! expectBody ok url response
  }

/// Echoes its own path, so a request proves which endpoint answered.
let private echo = fun (ctx: {| ctx: HttpContext |}) -> string ctx.ctx.Request.Path

type Marker(name: string) =
  member _.Name = name

let private markersOf (e: Endpoint) =
  e.Metadata.GetOrderedMetadata<Marker>() |> Seq.map _.Name |> List.ofSeq

[<Fact>]
let ``group prefixes every route`` () =
  task {
    use! app =
      serve (
        endpoints {
          group "users"
          get "/" echo
          get "/{id}" echo
        }
      )

    Assert.Equal<string list>([ "/users/"; "/users/{id}" ], routesOf app)
    do! answers app "/users/42"
  }

[<Fact>]
let ``route is the same as a group`` () =
  task {
    use! app = serve (route "users" { get "/all" echo })
    do! answers app "/users/all"
  }

[<Fact>]
let ``path joins segments, trimming their slashes`` () =
  task {
    use! app =
      serve (
        endpoints {
          path "/api/" "v1" "/users"
          get "/list" echo
        }
      )

    do! answers app "/api/v1/users/list"
  }

[<Fact>]
let ``path with one segment is a group`` () =
  task {
    use! app =
      serve (
        endpoints {
          path "/users/"
          get "/list" echo
        }
      )

    do! answers app "/users/list"
  }

[<Fact>]
let ``the last group or path wins`` () =
  task {
    use! app =
      serve (
        endpoints {
          group "first"
          path "second" "third"
          get "/x" echo
        }
      )

    Assert.Equal<string list>([ "/second/third/x" ], routesOf app)
  }

[<Fact>]
let ``nested endpoints inherit the parent group`` () =
  task {
    use! app =
      serve (
        endpoints {
          group "api"
          get "/root" echo

          endpoints {
            group "users"
            get "/{id}" echo

            route "profile" { get "/me" echo }
          }
        }
      )

    Assert.Equal<string list>([ "/api/root"; "/api/users/{id}"; "/api/users/profile/me" ], routesOf app)
    do! answers app "/api/root"
    do! answers app "/api/users/1"
    do! answers app "/api/users/profile/me"
  }

[<Fact>]
let ``sibling groups do not leak into each other`` () =
  task {
    use! app =
      serve (
        endpoints {
          endpoints {
            group "a"
            get "/x" echo
          }

          endpoints { route "nested" { get "/x" echo } }

          endpoints {
            group "b"
            get "/x" echo
          }

          route "c" { get "/x" echo }
        }
      )

    Assert.Equal<string list>([ "/a/x"; "/nested/x"; "/b/x"; "/c/x" ], routesOf app)
    do! answers app "/a/x"
    do! answers app "/b/x"
    do! answers app "/nested/x"
    do! answers app "/c/x"
  }

[<Fact>]
let ``nested endpoints without a group map at the parent's level`` () =
  task {
    use! app =
      serve (
        endpoints {
          group "api"
          endpoints { get "/one" echo }
          endpoints { get "/two" echo }
        }
      )

    Assert.Equal<string list>([ "/api/one"; "/api/two" ], routesOf app)
  }

// ASP.NET lists a group's own endpoints together, where the first of them was mapped,
// so only the order of the direct endpoints and of the subgroups is observable.
[<Fact>]
let ``endpoints are mapped in the order they are declared`` () =
  task {
    use! app =
      serve (
        endpoints {
          get "/1" echo
          route "g" { get "/2" echo }
          endpoints { get "/3" echo }
          route "g" { get "/4" echo }
          get "/5" echo
        }
      )

    Assert.Equal<string list>([ "/1"; "/5"; "/g/2"; "/3"; "/g/4" ], routesOf app)
  }

[<Fact>]
let ``subgroups declared first keep their place`` () =
  task {
    use! app =
      serve (
        endpoints {
          route "g" { get "/1" echo }
          get "/2" echo
          route "h" { get "/3" echo }
        }
      )

    Assert.Equal<string list>([ "/g/1"; "/2"; "/h/3" ], routesOf app)
  }

[<Fact>]
let ``operations after nested endpoints still apply to the parent`` () =
  task {
    use! app =
      serve (
        endpoints {
          route "child" { get "/x" echo }
          get "/y" echo
          group "parent"
          tags "Parent"
        }
      )

    Assert.Equal<string list>([ "/parent/child/x"; "/parent/y" ], routesOf app)
    Assert.Equal<string list>([ "Parent" ], tagsOf (endpoint app "GET" "/parent/child/x"))
    do! answers app "/parent/child/x"
  }

[<Fact>]
let ``a group containing only another group keeps both prefixes`` () =
  task {
    use! app = serve (route "outer" { route "inner" { get "/x" echo } })
    Assert.Equal<string list>([ "/outer/inner/x" ], routesOf app)
  }

[<Fact>]
let ``deeply nested groups compose`` () =
  task {
    use! app =
      serve (route "a" { route "b" { route "c" { route "d" { get "/e" echo } } } })

    Assert.Equal<string list>([ "/a/b/c/d/e" ], routesOf app)
    do! answers app "/a/b/c/d/e"
  }

[<Fact>]
let ``useRoutes maps another set of routes into this group`` () =
  task {
    let shared =
      endpoints {
        get "/health" echo
        get "/version" echo
      }

    use! app =
      serve (
        endpoints {
          route "a" { useRoutes shared }
          route "b" { useRoutes shared }
        }
      )

    Assert.Equal<string list>([ "/a/health"; "/a/version"; "/b/health"; "/b/version" ], routesOf app)
    do! answers app "/b/version"
  }

[<Fact>]
let ``Apply maps the routes onto any route builder`` () =
  task {
    let routes = route "users" { get "/{id}" echo }

    use! app =
      start (fun app ->
        app.MapGroup("api").WithTags("Root") |> routes.Apply |> ignore
        app.MapGroup("v2") |> routes.Apply |> ignore)

    Assert.Equal<string list>([ "/api/users/{id}"; "/v2/users/{id}" ], routesOf app)
    Assert.Equal<string list>([ "Root" ], tagsOf (endpoint app "GET" "/api/users/{id}"))
    Assert.Empty(tagsOf (endpoint app "GET" "/v2/users/{id}"))
    do! answers app "/v2/users/9"
  }

[<Fact>]
let ``apply maps the routes from inside the builder`` () =
  task {
    use! app =
      start (fun app ->
        endpoints {
          group "applied"
          get "/x" echo
          apply app
        })

    do! answers app "/applied/x"
  }

[<Fact>]
let ``tags and description apply to the whole group, including nested groups`` () =
  task {
    use! app =
      serve (
        endpoints {
          route "users" {
            tags "Users" "People"
            description "User endpoints"
            get "/" echo

            route "admin" {
              tags "Admin"
              get "/" echo
            }
          }

          get "/other" echo
        }
      )

    let users = endpoint app "GET" "/users/"
    let admin = endpoint app "GET" "/users/admin/"
    let other = endpoint app "GET" "/other"
    Assert.Equal<string list>([ "Users"; "People" ], tagsOf users)
    Assert.Equal<string list>([ "Users"; "People"; "Admin" ], tagsOf admin)
    Assert.Empty(tagsOf other)
    Assert.Equal<string list>([ "User endpoints" ], descriptionOf users)
    Assert.Equal<string list>([ "User endpoints" ], descriptionOf admin)
    Assert.Empty(descriptionOf other)
  }

[<Fact>]
let ``set configures the group builder`` () =
  task {
    use! app =
      serve (
        endpoints {
          route "marked" {
            set (fun group -> group.WithMetadata(Marker "first"))
            get "/x" echo
            set (fun group -> group.WithMetadata(Marker "second"))
          }

          get "/unmarked" echo
        }
      )

    Assert.Equal<string list>([ "first"; "second" ], markersOf (endpoint app "GET" "/marked/x") |> List.sort)
    Assert.Empty(markersOf (endpoint app "GET" "/unmarked"))
  }

[<Fact>]
let ``an empty builder maps nothing`` () =
  task {
    use! app = serve (endpoints { group "empty" })
    Assert.Empty(app.Endpoints)
    let! response = get app "/empty"
    let! _ = expect HttpStatusCode.NotFound response
    ()
  }