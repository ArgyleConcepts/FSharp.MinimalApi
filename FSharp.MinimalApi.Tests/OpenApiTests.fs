module OpenApiTests

open System
open System.Text.Json
open System.Text.Json.Nodes
open System.Text.Json.Serialization
open Expecto
open Json.Schema
open Microsoft.AspNetCore.Builder
open Microsoft.AspNetCore.Hosting
open Microsoft.AspNetCore.Http.Json
open Microsoft.AspNetCore.TestHost
open Microsoft.Extensions.DependencyInjection
open Microsoft.Extensions.Options
open FSharp.MinimalApi.OpenApi

type Fieldless =
  | A
  | B

type UnionValue =
  | ANumber of int
  | AString of string
  | Nothing

type Shape =
  | Circle of radius: float
  | Rect of width: float * height: float

type Email = Email of string

type Pair = Pair of left: int * right: string

type Payload = { Id: int; Label: string }

type WithRecord =
  | Wrapped of Payload
  | Blank

type Tree =
  | Leaf of int
  | Node of Tree list

type Renamed =
  | [<JsonName "first">] First
  | [<JsonName("val", Field = "value")>] Second of value: int

type Sample =
  {
    Name: string
    Nick: string option
    Count: int voption
    Tags: string list
    Ids: Set<int>
    Scores: Map<string, int>
    ByNumber: Map<int, string>
    Tuple: int * string
    Kind: Fieldless
    Value: UnionValue
    Shape: Shape
    Mail: Email
    Pair: Pair
    Nested: WithRecord
    Tree: Tree
    Renamed: Renamed
    Born: DateOnly
    [<JsonPropertyName "custom">]
    Original: int
    [<JsonName "aka">]
    Alias: string
    [<JsonIgnore>]
    Hidden: string
    Maybe: Skippable<int>
    Outcome: Result<int, string>
    Anonymous: {| Inner: int option |}
  }

let samples =
  let first =
    {
      Name = "name"
      Nick = Some "nick"
      Count = ValueSome 1
      Tags = [ "a"; "b" ]
      Ids = set [ 1; 2 ]
      Scores = Map [ "x", 1 ]
      ByNumber = Map [ 1, "one" ]
      Tuple = (1, "one")
      Kind = A
      Value = ANumber 1
      Shape = Circle 1.5
      Mail = Email "a@b"
      Pair = Pair(1, "one")
      Nested = Wrapped { Id = 1; Label = "label" }
      Tree = Node [ Leaf 1; Node [] ]
      Renamed = First
      Born = DateOnly(2000, 1, 2)
      Original = 1
      Alias = "alias"
      Hidden = "hidden"
      Maybe = Include 5
      Outcome = Ok 1
      Anonymous = {| Inner = Some 1 |}
    }

  [
    first
    { first with
        Nick = None
        Count = ValueNone
        Tags = []
        Kind = B
        Value = AString "s"
        Shape = Rect(1.0, 2.0)
        Nested = Blank
        Tree = Leaf 2
        Renamed = Second 3
        Maybe = Skip
        Outcome = Error "e"
        Anonymous = {| Inner = None |}
    }
    { first with Value = Nothing }
  ]

let encodings =
  [
    "Default", JsonFSharpOptions.Default()
    "NewtonsoftLike", JsonFSharpOptions.NewtonsoftLike()
    "ThothLike", JsonFSharpOptions.ThothLike()
    "FSharpLuLike", JsonFSharpOptions.FSharpLuLike()
    "AdjacentTag named fields", JsonFSharpOptions.Default().WithUnionNamedFields()
    "ExternalTag", JsonFSharpOptions.Default().WithUnionExternalTag()
    "ExternalTag named fields", JsonFSharpOptions.Default().WithUnionExternalTag().WithUnionNamedFields()
    "InternalTag", JsonFSharpOptions.Default().WithUnionInternalTag()
    "InternalTag named fields", JsonFSharpOptions.Default().WithUnionInternalTag().WithUnionNamedFields()
    "Untagged", JsonFSharpOptions.Default().WithUnionUntagged()
    "Unwrap fieldless tags", JsonFSharpOptions.Default().WithUnionUnwrapFieldlessTags()
    "Unwrap single field cases", JsonFSharpOptions.Default().WithUnionUnwrapSingleFieldCases()
    "Unwrap record cases", JsonFSharpOptions.Default().WithUnionNamedFields().WithUnionUnwrapRecordCases()
    "Tag naming policy", JsonFSharpOptions.Default().WithUnionTagNamingPolicy(JsonNamingPolicy.CamelCase)
    "Field naming policy",
    JsonFSharpOptions.Default().WithUnionNamedFields().WithUnionFieldNamingPolicy(JsonNamingPolicy.SnakeCaseLower)
    "Field names from types", JsonFSharpOptions.Default().WithUnionNamedFields().WithUnionFieldNamesFromTypes()
    "Map as array of pairs", JsonFSharpOptions.Default().WithMapFormat(MapFormat.ArrayOfPairs)
  ]

type Api =
  {
    Document: JsonNode
    Serializer: JsonSerializerOptions
  }

let createApiFor<'T> (fsharpOptions: JsonFSharpOptions) (sample: 'T) =
  task {
    let builder = WebApplication.CreateSlimBuilder()
    builder.WebHost.UseTestServer() |> ignore

    builder.Services.ConfigureHttpJsonOptions(fun o -> fsharpOptions.AddToJsonSerializerOptions o.SerializerOptions)
    |> ignore

    builder.Services.AddOpenApi(fun o -> o.AddFSharp fsharpOptions |> ignore)
    |> ignore

    let app = builder.Build()
    app.MapOpenApi() |> ignore
    app.MapGet("/sample", Func<'T>(fun () -> sample)) |> ignore

    try
      do! app.StartAsync()
      let! text = app.GetTestClient().GetStringAsync "/openapi/v1.json"

      let serializer =
        app.Services.GetRequiredService<IOptions<JsonOptions>>().Value.SerializerOptions

      return
        {
          Document = JsonNode.Parse text |> Option.ofObj |> Option.get
          Serializer = serializer
        }
    finally
      (app :> IAsyncDisposable).DisposeAsync().AsTask().Wait()
  }

let createApi fsharpOptions = createApiFor fsharpOptions samples.Head

let componentSchemas (api: Api) =
  match api.Document["components"] with
  | null -> JsonObject()
  | components -> components["schemas"].AsObject()

/// The response schema as a standalone JSON Schema, with components as $defs.
let responseSchema (api: Api) =
  let response =
    api.Document["paths"].["/sample"].["get"].["responses"].["200"].["content"].["application/json"].["schema"]

  let root = JsonObject()
  root["$schema"] <- JsonValue.Create "https://json-schema.org/draft/2020-12/schema"
  root["$defs"] <- (componentSchemas api).DeepClone()
  root["allOf"] <- JsonArray(response.DeepClone())
  JsonSchema.FromText(root.ToJsonString().Replace("#/components/schemas/", "#/$defs/"))

let validate (schema: JsonSchema) (json: string) =
  use document = JsonDocument.Parse json

  let results =
    schema.Evaluate(document.RootElement, EvaluationOptions(OutputFormat = OutputFormat.List))

  let failures =
    results.Details
    |> Seq.filter (fun d -> not d.IsValid && not (isNull d.Errors) && d.Errors.Count > 0)
    |> Seq.map (fun d -> $"{d.InstanceLocation} @ {d.EvaluationPath}: %A{List.ofSeq d.Errors.Values}")
    |> String.concat "\n"

  results.IsValid, failures

let encodingTests =
  encodings
  |> List.map (fun (name, fsharpOptions) ->
    testTask name {
      let! api = createApi fsharpOptions
      let schema = responseSchema api

      for sample in samples do
        let json = JsonSerializer.Serialize(sample, api.Serializer)
        let valid, details = validate schema json
        Expect.isTrue valid $"Serialized sample does not match the schema.\nJSON: {json}\nErrors: {details}"

      for KeyValue(id, definition) in componentSchemas api do
        Expect.isGreaterThan (definition.AsObject().Count) 0 $"Component '{id}' is an empty schema"
    })

let defaultEncodingTests =
  testList
    "Default encoding"
    [
      testTask "rejects JSON that does not match the F# types" {
        let! api = createApi (JsonFSharpOptions.Default())
        let schema = responseSchema api
        let json = JsonSerializer.SerializeToNode(samples.Head, api.Serializer).AsObject()

        let mutate (change: JsonObject -> unit) =
          let copy = json.DeepClone().AsObject()
          change copy
          copy.ToJsonString()

        let invalid =
          [
            "unknown union case", mutate (fun o -> o["kind"] <- JsonNode.Parse """{"Case":"C"}""")
            "missing required field", mutate (fun o -> o.Remove "name" |> ignore)
            "wrong field type", mutate (fun o -> o["tags"] <- JsonValue.Create 1)
            "unwrapped single-case union", mutate (fun o -> o["mail"] <- JsonNode.Parse """{"Case":"Email"}""")
            "wrong union fields", mutate (fun o -> o["value"] <- JsonNode.Parse """{"Case":"ANumber","Fields":["x"]}""")
          ]

        for case, text in invalid do
          let valid, _ = validate schema text
          Expect.isFalse valid $"Expected '{case}' to be rejected: {text}"
      }

      testTask "marks option fields optional and nullable" {
        let! api = createApi (JsonFSharpOptions.Default())
        let sample = (componentSchemas api)["Sample"]
        let required = sample["required"].AsArray() |> Seq.map string |> Set.ofSeq

        Expect.isTrue (required.Contains "name") "name is required"
        Expect.isFalse (required.Contains "nick") "an option field is not required"
        Expect.isFalse (required.Contains "maybe") "a Skippable field is not required"

        Expect.equal
          (sample["properties"].["nick"].ToJsonString())
          """{"type":["null","string"]}"""
          "an option of string is a nullable string"
      }

      testTask "names record fields the way they are serialized" {
        let! api = createApi (JsonFSharpOptions.Default())
        let json = JsonSerializer.SerializeToNode(samples.Head, api.Serializer).AsObject()
        let sample = (componentSchemas api)["Sample"]
        let properties = sample["properties"].AsObject()

        for name in [ "aka"; "custom" ] do
          Expect.isTrue (json.ContainsKey name) $"'{name}' is serialized"
          Expect.isTrue (properties.ContainsKey name) $"'{name}' is described"

        for name in [ "hidden"; "alias"; "original" ] do
          Expect.isFalse (json.ContainsKey name) $"'{name}' is not serialized"
          Expect.isFalse (properties.ContainsKey name) $"'{name}' is not described"
      }

      testTask "describes maps keyed by single-case unions as objects" {
        let sample = Map [ Email "a@b", 1 ]
        let! api = createApiFor (JsonFSharpOptions.Default()) sample
        let json = JsonSerializer.Serialize(sample, api.Serializer)
        let valid, details = validate (responseSchema api) json
        Expect.isTrue valid $"JSON: {json}\nErrors: {details}"
        Expect.stringStarts json "{" "serialized as a JSON object"
      }

      testTask "describes single-case unions as their wrapped value" {
        let! api = createApi (JsonFSharpOptions.Default())
        let email = (componentSchemas api)["Email"]
        Expect.equal (email.ToJsonString()) """{"type":"string"}""" "Email"
      }
    ]

[<Tests>]
let tests =
  testList "OpenApi" [ testList "encodings" encodingTests; defaultEncodingTests ]