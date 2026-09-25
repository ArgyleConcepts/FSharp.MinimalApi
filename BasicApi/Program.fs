open System
open System.ComponentModel
open System.Linq
open System.Text.Json.Serialization
open System.Threading.Tasks
open BasicApi
open BasicApi.Db
open BasicApi.Models
open Microsoft.AspNetCore.Builder
open Microsoft.AspNetCore.Mvc
open Microsoft.EntityFrameworkCore
open Microsoft.Extensions.DependencyInjection
open Microsoft.Extensions.Hosting
open Microsoft.Extensions.Logging
open FSharp.MinimalApi
open FSharp.MinimalApi.OpenApi
open FSharp.MinimalApi.Builder
open Microsoft.Extensions.Options
open Microsoft.AspNetCore.Http
open Microsoft.AspNetCore.Http.HttpResults
open Scalar.AspNetCore
open type TypedResults

let otherRoute =
    endpoints {
        group "other"

        get "union" (fun () ->
            match Random.Shared.Next(0, 3) with
            | 0 -> UnionValue.ANumber 42
            | 1 -> UnionValue.AString "Foo"
            | _ -> UnionValue.Nothing)
    }

[<NoComparison>]
type CustomParams =
    { [<FromRoute>]
      foo: int
      [<FromQuery>]
      bar: string
      [<FromServices>]
      logger: ILogger<MyDbContext> }

let getUser (req: {| userId: Guid; db: MyDbContext |}) : Task<Results<Ok<User>, NotFound>> =
    task {
        let! res = req.db.Users.Where(fun x -> x.Id = UserId req.userId).TryFirstAsync()

        match res with
        | Some user -> return Results2.first (Ok user)
        | None -> return Results2.second (NotFound())
    }

let routes =
    endpoints {
        get "/hello" (fun () -> "world")

        get "/ping/{x}" (fun (req: {| x: int |}) -> $"pong %d{req.x}")

        get "/inc/{v:int}" (fun (req: {| v: int; n: Nullable<int> |}) -> req.v + (req.n.GetValueOrDefault 1))

        // access route builder
        get "/secret" (fun () -> "I'm secret") (fun b -> b.ExcludeFromDescription())

        // manual set builder config
        set (fun b -> b.MapGet("/health", (fun () -> "healthy")))

        get "/params/{foo}" (fun (param: CustomParams) ->
            param.logger.LogInformation "Hello Params"
            $"route=%d{param.foo}; query=%s{param.bar}")

        // using options from DI
        get "/settings" (fun (req: {| options: IOptions<MyCustomSettings> |}) -> req.options.Value)

        get "/double/{v}" produces<Ok<int>> (fun (req: {| v: int |}) -> Ok(req.v * 2))

        endpoints {
            path "very" "long" "path" "dec"
            get "/" (fun (n: int) -> n - 1)
        }

        get "/even/{v}" produces<Ok<string>, BadRequest> (fun (req: {| v: int; logger: ILogger<obj> |}) ->
            (if req.v % 2 = 0 then
                 !!Ok("even number!")
             else
                 req.logger.LogInformation $"Odd number: %d{req.v}"
                 !!BadRequest()))

        get "/delay/{n}" produces<NoContent> (fun (req: {| n: int |}) ->
            async {
                do! min 10 req.n |> float |> TimeSpan.FromSeconds |> Async.Sleep
                return NoContent()
            })

        otherRoute

        endpoints {
            group "user"
            tags "Users"

            filter (fun ctx next ->
                task {
                    if ctx.HttpContext.Request.Headers.Authorization.ToString() = "BAD" then
                        return UnprocessableEntity() :> objnull
                    else
                        return! next ctx
                })

            get "/" produces<Ok<User array>> (fun (req: {| db: MyDbContext |}) ->
                task {
                    let! users = req.db.Users.ToArrayAsync()
                    return Ok(users)
                })

            get "/{userId}" getUser

            patch
                "/{userId}/name"
                produces<Ok<User>, NotFound, ValidationProblem>
                (fun
                    (req:
                        {| userId: Guid
                           rename: RenameUser
                           db: MyDbContext |}) ->
                    task {
                        let! existing = req.db.Users.TryFirstAsync(fun x -> x.Id = UserId req.userId)

                        match existing with
                        | None -> return !!NotFound()
                        | Some user ->
                            let nameErrors = UserName.errors req.rename.Name

                            if nameErrors.Length > 0 then
                                return !!ValidationProblem(dict [ nameof req.rename.Name, nameErrors ])
                            else
                                let changed = { user with Name = req.rename.Name }
                                req.db.Entry(user).CurrentValues.SetValues(changed)
                                do! req.db.saveChangesAsync ()
                                return !!Ok(changed)
                    })
                (fun (b: RouteHandlerBuilder) -> b.WithName("rename-user").WithSummary("Rename a user"))

            route "profile" {
                allowAnonymous

                post
                    "/"
                    produces<Created<User>, Conflict, ValidationProblem>
                    (fun (req: {| userInfo: NewUser; db: MyDbContext |}) ->
                        task {
                            match NewUser.parseUser req.userInfo with
                            | Error err -> return !!ValidationProblem(err)
                            | Ok newUser ->
                                let! exists = req.db.Users.TryFirstAsync(fun x -> x.Email = newUser.Email)

                                match exists with
                                | Some _ -> return !!Conflict()
                                | None ->
                                    req.db.Users.add newUser
                                    do! req.db.saveChangesAsync ()
                                    return !!Created($"/user/%O{newUser.Id.Value}", newUser)
                        })

                delete "/{userId}" produces<NoContent, NotFound> (fun (req: {| userId: Guid; db: MyDbContext |}) ->
                    task {
                        let! exists = req.db.Users.TryFirstAsync(fun x -> x.Id = UserId req.userId)

                        match exists with
                        | None -> return !!NotFound()
                        | Some user ->
                            req.db.Users.remove user
                            do! req.db.saveChangesAsync ()
                            return !!NoContent()
                    })

            }
        }
    }

// basic support for option, single union and fieldless unions (for appsettings)
TypeDescriptor.addUnionTypesInAssemblyContaining<MyDbContext>
TypeDescriptor.addDefaultOptionTypes ()

[<EntryPoint>]
let main args =
    let builder = WebApplication.CreateBuilder(args)
    let jsonFsharp = JsonFSharpOptions.Default()

    builder.Services
        .ConfigureHttpJsonOptions(fun c -> jsonFsharp.AddToJsonSerializerOptions c.SerializerOptions)
        .AddSingleton(jsonFsharp)
        .AddOpenApi(fun o -> o.AddFSharp jsonFsharp |> ignore)
        .AddTuples()
        .AddDbContext<MyDbContext>(
            (fun c -> c.UseInMemoryDatabase("basic_api") |> ignore),
            ServiceLifetime.Singleton,
            ServiceLifetime.Singleton
        )
    |> ignore

    builder.Services.AddOptions<MyCustomSettings>().BindConfiguration("MyCustomSettings").ValidateOnStart()
    |> ignore

    let app = builder.Build()
    app.MapOpenApi() |> ignore
    app.MapScalarApiReference() |> ignore

    app.MapGroup("api").WithTags("Root") |> routes.Apply |> ignore

    app.Services.GetRequiredService<MyDbContext>().Database.EnsureCreated()
    |> ignore

    app.Run()
    0
