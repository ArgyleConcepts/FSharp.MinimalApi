# FSharp.MinimalApi

This is the Argyle Concepts maintained .NET 10 fork of [FSharp.MinimalApi](https://github.com/lucasteles/FSharp.MinimalApi), originally created by Lucas Teles. The original MIT copyright and permission notice are retained in [LICENSE](LICENSE). This fork's source, issues, and pull requests live in [ArgyleConcepts/FSharp.MinimalApi](https://github.com/ArgyleConcepts/FSharp.MinimalApi).

The Argyle packages are being prepared locally and have **not been published**.

Easily define your routes in your [ASP.NET Core MinimalAPI](https://learn.microsoft.com/en-us/aspnet/core/fundamentals/minimal-apis) with [`TypedResults`](https://learn.microsoft.com/en-us/aspnet/core/release-notes/aspnetcore-7.0?view=aspnetcore-7.0#typed-results-for-minimal-apis) support

## Getting started

The planned packages are `ArgyleConcepts.FSharp.MinimalApi` and the optional `ArgyleConcepts.FSharp.MinimalApi.OpenApi`, both version `0.3.0`. The Core package includes the Interop assembly, so there is no separate Interop package. Until a release is approved, use project references or the locally packed packages from [Package build and release readiness](#package-build-and-release-readiness).

See the complete [BasicApi sample](BasicApi/Program.fs).


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

## Typed outcomes from named F# handlers

For a named handler, annotate its concrete return type and map it directly. The result type gives ASP.NET Core the declared response metadata, and the compiler rejects any branch that returns an outcome outside that type.

Before, an inline handler needed a `produces` witness and implicit conversions:

```fsharp
get "/users/{id}" produces<Ok<User>, NotFound> (fun (req: {| id: int |}) ->
    task {
        match findUser req.id with
        | Some user -> return !!Ok user
        | None -> return !!NotFound()
    })
```

With a named handler, the return annotation supplies the two outcomes. `Results2.first` and `Results2.second` construct the corresponding ASP.NET Core `Results<_,_>` case without changing its type or serialization:

```fsharp
open System.Threading.Tasks
open Microsoft.AspNetCore.Http
open Microsoft.AspNetCore.Http.HttpResults
open FSharp.MinimalApi.Builder
open type TypedResults

let getUser (req: {| id: int |}) : Task<Results<Ok<User>, NotFound>> =
    task {
        match findUser req.id with
        | Some user -> return Results2.first (Ok user)
        | None -> return Results2.second (NotFound())
    }

let routes = endpoints { get "/users/{id}" getUser }
```

The same mapping works for synchronous and `Async` handlers with an annotated `Results<_,_>` return type. Keep `produces<...>` for inline handlers where the annotation improves inference, for handlers with more than two outcomes, or when keeping an existing declaration. A custom `IResult` can also return pre-serialized bytes unchanged. If it does not provide its own endpoint metadata, declare the response through the optional endpoint config argument:

```fsharp
get "/raw/{id}" rawJson (fun (b: RouteHandlerBuilder) -> b.Produces(200, "application/json"))
```

## Pull request validation

[`azure-pipelines.yml`](azure-pipelines.yml) validates GitHub pull requests into `develop` through the **FSharp.MinimalApi PR Validation** pipeline in the **ArgyleConceptsLLC** Azure DevOps organization. It does not run on pushes or publish packages. The pipeline installs the SDK selected by `global.json`, restores the solution and local tools, builds in Release with warnings as errors, runs the F# analyzers, runs the solution tests, checks F# formatting with Fantomas, and verifies locally packed packages.

To reproduce the validation locally, run these commands from the repository root:

```sh
dotnet restore FSharp.MinimalApi.sln
dotnet tool restore
dotnet build FSharp.MinimalApi.sln --configuration Release --no-restore
dotnet fsi eng/fsharp-analysis/Run.fsx
dotnet test --solution FSharp.MinimalApi.sln --configuration Release --no-build --no-restore
dotnet fantomas check .
bash eng/verify-packages.sh
```

Maintainer setup:

1. The Azure pipeline uses the existing **ArgyleConcepts** GitHub App service connection and `azure-pipelines.yml`. Confirm that the connection has access to `ArgyleConcepts/FSharp.MinimalApi` and permission to post PR checks; keep its credentials in Azure DevOps. After this YAML is merged, set the pipeline's default branch to `develop`.
2. Open a PR targeting `develop` and confirm Azure Pipelines starts automatically and posts a successful check. Changes to that PR should start another run.
3. In the GitHub repository settings, protect `develop` and require the **FSharp.MinimalApi PR Validation** check from Azure Pipelines. Require the branch to be up to date before merging. A failed build, analysis, formatting or test run must block the PR.

Package publishing remains a separate release decision.

## Analyzers and warnings

- Every project builds with `Nullable` enabled and warnings as errors. For F# projects `Nullable` also turns on the compiler's nullness checks, and warning level 5 plus the opt-in warnings FS0052, FS1178, FS3389, FS3390, FS3559, FS3570, FS3579, FS3582 and FS3878 apply.
- C# projects use Meziantou.Analyzer, the banned API analyzer with the lists in `eng/analyzers`, and the Visual Studio threading analyzers, with rule severities in `.editorconfig`. The public API analyzer tracks `FSharp.MinimalApi.Interop`, which ships inside the Core package.
- `eng/fsharp-analysis/Run.fsx` checks that every project is built by the solution, runs the Ionide, G-Research and WoofWare analyzers, bans partial collection and option functions, and runs curated FSharpLint rules. Reports go to `fsharp-analysis-results/`.
- The package smoke consumer in `eng/PackageSmoke` also builds with nullness checks and warnings as errors.

### Nullness for consumers

The assemblies now carry nullness metadata. Projects that do not enable nullness checks see no change. Projects that do should note:

- `filter` functions receive and return `ValueTask<objnull>` or `Task<objnull>`, because an endpoint result can be null.
- ASP.NET Core treats non-nullable handler parameters and fields as required. A missing body or header for such a field is a bad request; declare it as `string | null` (or another nullable type) to make it optional.

## Package build and release readiness

`Directory.Build.props` sets the shared `0.3.0` package version and the `FSharp.Core` 10.0.100 minimum used by every project. The Core package contains both `FSharp.MinimalApi.dll` and its implementation-only `FSharp.MinimalApi.Interop.dll`; the OpenAPI package is separate. Both include the README and the original MIT LICENSE. The package project and repository URLs point to this fork, while the README credits Lucas Teles and links to the upstream project.

Run `bash eng/verify-packages.sh` to pack both packages into a temporary local directory, inspect their metadata and contents, then restore and run a small F# app against those packages. The temporary files are deleted when the check finishes. To retain packages for inspection, run:

```sh
dotnet pack FSharp.MinimalApi/FSharp.MinimalApi.fsproj --configuration Release --output artifacts/packages
dotnet pack FSharp.MinimalApi.OpenApi/FSharp.MinimalApi.OpenApi.fsproj --configuration Release --output artifacts/packages
```

The intended NuGet owner is an Argyle Concepts organization account. The Argyle packages have not been published. Before any release, review the two `.nupkg` and `.snupkg` files, confirm the version and source links, rerun the validation commands above, set up organization ownership and publishing credentials outside this repository, and make a separate release decision. The PR pipeline has no publishing step or credentials.

## OpenAPI

`FSharp.MinimalApi.OpenApi` makes [Microsoft.AspNetCore.OpenApi](https://learn.microsoft.com/en-us/aspnet/core/fundamentals/openapi/overview) describe F# types the way [FSharp.SystemTextJson](https://github.com/Tarmil/FSharp.SystemTextJson) serializes them. Without it, records, unions, options and F# collections appear as empty schemas, because FSharp.SystemTextJson's converters hide their structure from the generator.

Its planned package ID is `ArgyleConcepts.FSharp.MinimalApi.OpenApi`.

Pass the same `JsonFSharpOptions` you use for serialization:

Register both `ConfigureHttpJsonOptions` and `AddOpenApi` when using `AddFSharp`. The JSON converter must be installed before OpenAPI schema generation; otherwise document generation throws an `InvalidOperationException` with setup guidance.

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

## Development

The tests use xUnit v3 on Microsoft Testing Platform:

```ps
$ dotnet test
```

`./fake.sh test` also collects coverage and fails when a library assembly drops below 90% line coverage. `./fake.sh lint` checks formatting.
