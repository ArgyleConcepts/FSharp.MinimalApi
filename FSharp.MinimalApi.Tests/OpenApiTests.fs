module OpenApiTests

open System
open System.Text.Json
open System.Text.Json.Nodes
open System.Text.Json.Serialization
open Json.Schema
open Microsoft.AspNetCore.Builder
open Microsoft.AspNetCore.Http.Json
open Microsoft.Extensions.DependencyInjection
open Microsoft.Extensions.Options
open Xunit
open FSharp.MinimalApi.OpenApi
open Harness

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
  dict
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
    use! app =
      startWith
        (fun services ->
          services.ConfigureHttpJsonOptions(fun o -> fsharpOptions.AddToJsonSerializerOptions o.SerializerOptions)
          |> ignore

          services.AddOpenApi(fun o -> o.AddFSharp fsharpOptions |> ignore) |> ignore)
        (fun app ->
          app.MapOpenApi() |> ignore
          app.MapGet("/sample", Func<'T>(fun () -> sample)) |> ignore)

    let! text =
      app.Client.GetStringAsync("/openapi/v1.json", TestContext.Current.CancellationToken)

    return
      {
        Document = JsonNode.Parse text |> Option.ofObj |> Option.get
        Serializer = app.Services.GetRequiredService<IOptions<JsonOptions>>().Value.SerializerOptions
      }
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

let encodingNames = TheoryData<string>(encodings.Keys)

[<Theory>]
[<MemberData(nameof encodingNames)>]
let ``serialized values match the schema for every encoding`` (encoding: string) =
  task {
    let! api = createApi encodings[encoding]
    let schema = responseSchema api

    for sample in samples do
      let json = JsonSerializer.Serialize(sample, api.Serializer)
      let valid, details = validate schema json
      Assert.True(valid, $"Serialized sample does not match the schema.\nJSON: {json}\nErrors: {details}")

    for KeyValue(id, definition) in componentSchemas api do
      Assert.True(definition.AsObject().Count > 0, $"Component '{id}' is an empty schema")
  }

[<Fact>]
let ``rejects JSON that does not match the F# types`` () =
  task {
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
      Assert.False(valid, $"Expected '{case}' to be rejected: {text}")
  }

[<Fact>]
let ``marks option fields optional and nullable`` () =
  task {
    let! api = createApi (JsonFSharpOptions.Default())
    let sample = (componentSchemas api)["Sample"]
    let required = sample["required"].AsArray() |> Seq.map string |> Set.ofSeq

    Assert.True(required.Contains "name", "name is required")
    Assert.False(required.Contains "nick", "an option field is not required")
    Assert.False(required.Contains "maybe", "a Skippable field is not required")
    Assert.Equal("""{"type":["null","string"]}""", sample["properties"].["nick"].ToJsonString())
  }

[<Fact>]
let ``names record fields the way they are serialized`` () =
  task {
    let! api = createApi (JsonFSharpOptions.Default())
    let json = JsonSerializer.SerializeToNode(samples.Head, api.Serializer).AsObject()
    let sample = (componentSchemas api)["Sample"]
    let properties = sample["properties"].AsObject()

    for name in [ "aka"; "custom" ] do
      Assert.True(json.ContainsKey name, $"'{name}' is serialized")
      Assert.True(properties.ContainsKey name, $"'{name}' is described")

    for name in [ "hidden"; "alias"; "original" ] do
      Assert.False(json.ContainsKey name, $"'{name}' is not serialized")
      Assert.False(properties.ContainsKey name, $"'{name}' is not described")
  }

[<Fact>]
let ``describes maps keyed by single-case unions as objects`` () =
  task {
    let sample = Map [ Email "a@b", 1 ]
    let! api = createApiFor (JsonFSharpOptions.Default()) sample
    let json = JsonSerializer.Serialize(sample, api.Serializer)
    let valid, details = validate (responseSchema api) json
    Assert.True(valid, $"JSON: {json}\nErrors: {details}")
    Assert.StartsWith("{", json)
  }

[<Fact>]
let ``describes single-case unions as their wrapped value`` () =
  task {
    let! api = createApi (JsonFSharpOptions.Default())
    let email = (componentSchemas api)["Email"]
    Assert.Equal("""{"type":"string"}""", email.ToJsonString())
  }