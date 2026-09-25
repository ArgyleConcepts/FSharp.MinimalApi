open FSharp.MinimalApi.Builder
open FSharp.MinimalApi.OpenApi
open Microsoft.AspNetCore.Builder
open Microsoft.Extensions.DependencyInjection
open System.Text.Json.Serialization

let jsonFSharp = JsonFSharpOptions.Default()
let builder = WebApplication.CreateBuilder([||])

builder.Services
    .ConfigureHttpJsonOptions(fun options -> jsonFSharp.AddToJsonSerializerOptions options.SerializerOptions)
    .AddOpenApi(fun options -> options.AddFSharp jsonFSharp |> ignore)
|> ignore

let app = builder.Build()
let routes = endpoints { get "/ping" (fun () -> "pong") }
routes.Apply app |> ignore
app.MapOpenApi() |> ignore

printfn "Packed Core and OpenAPI assemblies loaded and mapped an F# endpoint."
