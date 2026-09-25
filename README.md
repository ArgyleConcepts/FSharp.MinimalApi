[![CI](https://github.com/lucasteles/FSharp.MinimalApi/actions/workflows/ci.yml/badge.svg)](https://github.com/lucasteles/FSharp.MinimalApi/actions/workflows/ci.yml)
[![Nuget](https://img.shields.io/nuget/v/FSharp.MinimalApi.svg?style=flat)](https://www.nuget.org/packages/FSharp.MinimalApi)

# FSharp.MinimalApi 

Easily define your routes in your [ASP.NET Core MinimalAPI](https://learn.microsoft.com/en-us/aspnet/core/fundamentals/minimal-apis) with [`TypedResults`](https://learn.microsoft.com/en-us/aspnet/core/release-notes/aspnetcore-7.0?view=aspnetcore-7.0#typed-results-for-minimal-apis) support

## Getting started

[NuGet package](https://www.nuget.org/packages/FSharp.MinimalApi) available:

```ps
$ dotnet add package FSharp.MinimalApi
```

> **💡** You can check a complete sample [HERE](https://github.com/lucasteles/FSharp.MinimalApi/tree/master/BasicApi)


## Defining Routes

```fsharp
open Microsoft.AspNetCore.Http
open Microsoft.AspNetCore.Http.HttpResults

open FSharp.MinimalApi
open FSharp.MinimalApi.Builder
open type TypedResults

type CustomParams =
    { [<FromRoute>]
      foo: int
      [<FromQuery>]
      bar: string
      [<FromServices>]
      logger: ILogger<MyDbContext> }

let routes =
    endpoints {
        get "/hello" (fun () -> "world")

        // request bindable parameters must be mapped to objects/records
        get "/ping/{x}" (fun (req: {| x: int |}) -> $"pong {req.x}")

        get "/inc/{v:int}" (fun (req: {| v: int; n: Nullable<int> |}) -> req.v + (req.n.GetValueOrDefault 1))

        get "/params/{foo}" (fun (param: CustomParams) ->
            param.logger.LogInformation "Hello Params"
            $"route={param.foo}; query={param.bar}")

        // better static/openapi typing
        get "/double/{v}" produces<Ok<int>> (fun (req: {| v: int |}) -> Ok(req.v * 2))

        get "/even/{v}" produces<Ok<string>, BadRequest> (fun (req: {| v: int; logger: ILogger<_> |}) ->
            (if req.v % 2 = 0 then
                 // TypedResult relies havely on implict convertions
                 // the (!!) operator help us to call the implicit cast
                 !! Ok("even number!")
             else
                 req.logger.LogInformation $"Odd number: {req.v}"
                 !! BadRequest()))
        
        // nesting
        endpoints {
            group "user"
            tags "Users"

            get "/" produces<Ok<User[]>> (fun (req: {| db: MyDbContext |}) ->
                task {
                    let! users = req.db.Users.ToArrayAsync()
                    return Ok(users)
                })

            get "/{userId}" produces<Ok<User>, NotFound> (fun (req: {| userId: Guid; db: MyDbContext |}) ->
                task {
                    let! res = req.db.Users.Where(fun x -> x.Id = UserId req.userId).TryFirstAsync()

                    match res with
                    | Some user -> return !! Ok(user)
                    | None -> return !! NotFound()
                })

            // group mappping
            route "profile" {
                allowAnonymous

                post
                    "/"
                    produces<Created<User>, Conflict, ValidationProblem>
                    (fun (req: {| userInfo: NewUser; db: MyDbContext |}) ->
                        task {
                            match NewUser.parseUser req.userInfo with
                            | Error err -> return !! ValidationProblem(err)
                            | Ok newUser ->
                                let! exists = req.db.Users.TryFirstAsync(fun x -> x.Email = newUser.Email)

                                match exists with
                                | Some _ -> return !! Conflict()
                                | None ->
                                    req.db.Users.add newUser
                                    do! req.db.saveChangesAsync ()
                                    return !! Created($"/user/{newUser.Id.Value}", newUser)
                        })

                delete "/{userId}" produces<NoContent, NotFound> (fun (req: {| userId: Guid; db: MyDbContext |}) ->
                    task {
                        let! exists = req.db.Users.TryFirstAsync(fun x -> x.Id = UserId req.userId)

                        match exists with
                        | None -> return !! NotFound()
                        | Some user ->
                            req.db.Users.remove user
                            do! req.db.saveChangesAsync ()
                            return !! NoContent()
                    })

            }
        }
    }

[<EntryPoint>]
let main args =
    let builder = WebApplication.CreateBuilder(args)
    // ... builder configuration ...
    app.MapGroup("api").WithTags("Root") |> routes.Apply |> ignore
    // ... app configuration ...
    app.Run()
    0
```

## Pull request validation

[`azure-pipelines.yml`](azure-pipelines.yml) validates GitHub pull requests into `develop` in the **Argyle Converge** project of the **ArgyleConceptsLLC** Azure DevOps organization. It does not run on pushes or publish packages. The pipeline installs the SDK selected by `global.json`, restores the solution and local tools, builds in Release, runs the solution tests, and checks F# formatting with Fantomas.

To reproduce the validation locally, run these commands from the repository root:

```sh
dotnet restore FSharp.MinimalApi.sln
dotnet tool restore
dotnet build FSharp.MinimalApi.sln --configuration Release --no-restore
dotnet test FSharp.MinimalApi.sln --configuration Release --no-build --no-restore
dotnet fantomas check .
```

Maintainer setup:

1. In Azure DevOps, create a YAML pipeline for `ArgyleConcepts/FSharp.MinimalApi`, select `develop` and `azure-pipelines.yml`, and authorize the existing **ArgyleConcepts** GitHub App service connection for the pipeline. The service connection must have access to this repository and permission to post PR checks; keep its credentials in Azure DevOps.
2. Open a PR targeting `develop` and confirm Azure Pipelines starts automatically and posts a successful check. Changes to that PR should start another run.
3. In the GitHub repository settings, protect `develop` and require the Azure Pipelines check observed on the PR. Configure it to require the current commit's check before merging. Verify that a failed formatting or test run blocks the PR, then restore the passing commit.

Package build and publication metadata are being finalized in FSMAPI-4. Package publishing remains a separate release decision.

## OpenAPI

`FSharp.MinimalApi.OpenApi` makes [Microsoft.AspNetCore.OpenApi](https://learn.microsoft.com/en-us/aspnet/core/fundamentals/openapi/overview) describe F# types the way [FSharp.SystemTextJson](https://github.com/Tarmil/FSharp.SystemTextJson) serializes them. Without it, records, unions, options and F# collections appear as empty schemas, because FSharp.SystemTextJson's converters hide their structure from the generator.

```ps
$ dotnet add package FSharp.MinimalApi.OpenApi
```

Pass the same `JsonFSharpOptions` you use for serialization:

```fsharp
open System.Text.Json.Serialization
open FSharp.MinimalApi.OpenApi

let jsonFSharp = JsonFSharpOptions.Default()

builder.Services
    .ConfigureHttpJsonOptions(fun o -> jsonFSharp.AddToJsonSerializerOptions o.SerializerOptions)
    .AddOpenApi(fun o -> o.AddFSharp jsonFSharp |> ignore)
|> ignore

app.MapOpenApi() |> ignore
```

What it describes:

- **Records and anonymous records** as objects. Option, voption and `Skippable` fields are optional; `JsonName`, `JsonPropertyName` and `JsonIgnore` are respected.
- **Unions** following the configured `JsonUnionEncoding`: adjacent, external or internal tag, untagged, named fields, unwrapped fieldless tags, single-field cases, record cases and single-case unions, plus `JsonName` on cases and tag and field naming policies.
- **Options and voptions** inline as nullable values; **lists, sets, arrays, maps and tuples** inline as JSON arrays and objects.
- **Recursive types** through named components.

Not supported: per-type overrides (`JsonFSharpConverter` attributes or `WithOverrides`) and `IncludeRecordProperties`.
