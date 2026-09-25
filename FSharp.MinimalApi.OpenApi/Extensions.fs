[<AutoOpen>]
module FSharp.MinimalApi.OpenApi.Extensions

open System
open System.Text.Json.Serialization
open System.Text.Json.Serialization.Metadata
open Microsoft.AspNetCore.OpenApi

type OpenApiOptions with

    /// <summary>
    ///     Describes F# records, unions, options and collections the way FSharp.SystemTextJson serializes them.
    /// </summary>
    /// <param name="fsharpOptions">The same options passed to FSharp.SystemTextJson for HTTP JSON serialization.</param>
    member options.AddFSharp(fsharpOptions: JsonFSharpOptions) =
        let createDefault = options.CreateSchemaReferenceId

        options.CreateSchemaReferenceId <-
            Func<JsonTypeInfo, string | null>(fun typeInfo ->
                JsonConfiguration.validate fsharpOptions typeInfo.Options

                if FSharpShape.isInline typeInfo.Type then
                    null
                else
                    createDefault.Invoke typeInfo)

        options.AddSchemaTransformer(FSharpSchemaTransformer fsharpOptions)

    /// <summary>
    ///     Describes F# types using FSharp.SystemTextJson's default options.
    /// </summary>
    member options.AddFSharp() =
        options.AddFSharp(JsonFSharpOptions.Default())
