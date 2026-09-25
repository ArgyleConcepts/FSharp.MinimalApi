namespace FSharp.MinimalApi.OpenApi

open System
open System.Text.Json
open System.Text.Json.Serialization

module internal JsonConfiguration =
    // Compare values, so independently constructed equivalent options remain valid.
    // Custom naming policies and override functions must be shared by both registrations.
    let private settings (options: JsonFSharpOptions) =
        [| box options.UnionEncoding
           box options.UnionTagName
           box options.UnionFieldsName
           box options.UnionTagNamingPolicy
           box options.UnionFieldNamingPolicy
           box options.UnionTagCaseInsensitive
           box options.AllowNullFields
           box options.IncludeRecordProperties
           box options.SkippableOptionFields
           box options.MapFormat
           box options.Types
           box options.AllowOverride
           box options.Overrides |]

    let validate (expected: JsonFSharpOptions) (serializerOptions: JsonSerializerOptions) =
        let converters =
            serializerOptions.Converters
            |> Seq.choose (function
                | :? JsonFSharpConverter as converter -> Some converter
                | _ -> None)
            |> Seq.toArray

        if converters.Length = 0 then
            invalidOp
                "FSharp.MinimalApi.OpenApi requires the FSharp.SystemTextJson converter. Register JsonFSharpOptions with ConfigureHttpJsonOptions and pass the same options to AddFSharp before generating an OpenAPI document."

        for converter in converters do
            if
                not (Array.forall2 (fun a b -> Object.Equals(a, b)) (settings expected) (settings converter.Options))
            then
                invalidOp
                    "FSharp.MinimalApi.OpenApi: FSharp.SystemTextJson options do not match AddFSharp. Pass the same JsonFSharpOptions to ConfigureHttpJsonOptions (AddToJsonSerializerOptions) and AddFSharp."
