namespace FSharp.MinimalApi.OpenApi

open System
open System.Collections.Generic
open System.Collections.Immutable
open System.Reflection
open System.Text.Json
open System.Text.Json.Nodes
open System.Text.Json.Serialization
open System.Threading
open System.Threading.Tasks
open Microsoft.AspNetCore.OpenApi
open Microsoft.Extensions.DependencyInjection
open Microsoft.Extensions.Options
open Microsoft.FSharp.Reflection
open Microsoft.OpenApi

module internal Schema =
    let private schemaIdKey = "x-schema-id"

    let nullSchema () =
        OpenApiSchema(Type = Nullable JsonSchemaType.Null) :> IOpenApiSchema

    let constant (value: JsonNode) =
        let schema = OpenApiSchema()

        schema.Type <-
            Nullable(
                match value.GetValueKind() with
                | JsonValueKind.Number -> JsonSchemaType.Integer
                | JsonValueKind.True
                | JsonValueKind.False -> JsonSchemaType.Boolean
                | _ -> JsonSchemaType.String
            )

        schema.Enum <- List<JsonNode>([ value ])
        schema :> IOpenApiSchema

    /// A schema that the generator will emit as a $ref to a named component.
    let isComponent (schema: IOpenApiSchema) =
        match schema with
        | :? OpenApiSchemaReference -> true
        | :? OpenApiSchema as schema ->
            match schema.Metadata with
            | null -> false
            | metadata ->
                match metadata.TryGetValue schemaIdKey with
                | true, (:? string as id) -> not (String.IsNullOrEmpty id)
                | _ -> false
        | _ -> false

    /// <summary>
    ///     Refers to a component by id. The generator replaces it with a $ref once the component exists, which is how it
    ///     represents recursive types; an OpenApiSchemaReference here would make it follow the cycle forever.
    /// </summary>
    let componentPlaceholder (id: string) =
        OpenApiSchema(Metadata = Dictionary<string, obj>(dict [ schemaIdKey, id :> obj ])) :> IOpenApiSchema

    /// <summary>
    ///     Copies an inline schema's structure into the schema being transformed.
    /// </summary>
    /// <remarks>
    ///     Only values that are set are copied: assigning null to some properties (such as Const) makes the
    ///     serializer emit them, and <c>"const": null</c> would restrict the schema to null.
    /// </remarks>
    let copyInto (source: IOpenApiSchema) (target: OpenApiSchema) =
        let set (value: 'T | null) (assign: 'T -> unit) =
            match value with
            | null -> ()
            | value -> assign value

        let setValue (value: Nullable<'T>) (assign: Nullable<'T> -> unit) =
            if value.HasValue then
                assign value

        setValue source.Type (fun v -> target.Type <- v)
        set source.Format (fun v -> target.Format <- v)
        set source.Pattern (fun v -> target.Pattern <- v)
        set source.Const (fun v -> target.Const <- v)
        set source.Enum (fun v -> target.Enum <- v)
        set source.Items (fun v -> target.Items <- v)
        setValue source.MinItems (fun v -> target.MinItems <- v)
        setValue source.MaxItems (fun v -> target.MaxItems <- v)
        setValue source.UniqueItems (fun v -> target.UniqueItems <- v)
        set source.Properties (fun v -> target.Properties <- v)
        set source.Required (fun v -> target.Required <- v)
        set source.AdditionalProperties (fun v -> target.AdditionalProperties <- v)
        set source.OneOf (fun v -> target.OneOf <- v)
        set source.AnyOf (fun v -> target.AnyOf <- v)
        set source.AllOf (fun v -> target.AllOf <- v)
        set source.Minimum (fun v -> target.Minimum <- v)
        set source.Maximum (fun v -> target.Maximum <- v)
        set source.ExclusiveMinimum (fun v -> target.ExclusiveMinimum <- v)
        set source.ExclusiveMaximum (fun v -> target.ExclusiveMaximum <- v)
        setValue source.MinLength (fun v -> target.MinLength <- v)
        setValue source.MaxLength (fun v -> target.MaxLength <- v)

    /// Makes the target describe the inner schema or null.
    let nullableOf (inner: IOpenApiSchema) (target: OpenApiSchema) =
        if not (isComponent inner) && inner.Type.HasValue then
            copyInto inner target
            target.Type <- Nullable(inner.Type.Value ||| JsonSchemaType.Null)
        else
            target.OneOf <- List<IOpenApiSchema>([ nullSchema (); inner ])

    /// Makes the target describe exactly the inner schema.
    let sameAs (inner: IOpenApiSchema) (target: OpenApiSchema) =
        if isComponent inner then
            target.AllOf <- List<IOpenApiSchema>([ inner ])
        else
            copyInto inner target

    let array (items: IOpenApiSchema) =
        OpenApiSchema(Type = Nullable JsonSchemaType.Array, Items = items)

    /// A fixed-length JSON array. OpenAPI's object model has no prefixItems, so positions share one item schema.
    let tuple (elements: IOpenApiSchema list) =
        let schema = OpenApiSchema(Type = Nullable JsonSchemaType.Array)
        schema.MinItems <- Nullable elements.Length
        schema.MaxItems <- Nullable elements.Length

        match elements with
        | [] -> ()
        | [ single ] -> schema.Items <- single
        | many -> schema.Items <- OpenApiSchema(AnyOf = List<IOpenApiSchema>(many))

        schema

    let object (properties: (string * IOpenApiSchema * bool) list) =
        let schemaProperties = Dictionary<string, IOpenApiSchema>()
        let requiredProperties = HashSet<string>()

        for name, property, required in properties do
            schemaProperties[name] <- property

            if required then
                requiredProperties.Add name |> ignore

        OpenApiSchema(
            Type = Nullable JsonSchemaType.Object,
            Properties = schemaProperties,
            Required = requiredProperties
        )

/// <summary>
///     Describes F# types the way FSharp.SystemTextJson serializes them.
/// </summary>
/// <remarks>
///     FSharp.SystemTextJson serializes F# types with custom converters, which hide their structure from
///     Microsoft.AspNetCore.OpenApi, so records, unions, options and F# collections otherwise appear as empty schemas.
///     Pass the same <see cref="JsonFSharpOptions" /> used for serialization.
/// </remarks>
type FSharpSchemaTransformer(options: JsonFSharpOptions) =
    /// Types whose schema is being built on the current async flow, to stop recursive types from looping.
    static let building = AsyncLocal<ImmutableHashSet<Type> | null>()

    let encoding = options.UnionEncoding
    let has (flag: JsonUnionEncoding) = encoding.HasFlag flag

    let isAdjacentTag = has JsonUnionEncoding.AdjacentTag
    let isExternalTag = has JsonUnionEncoding.ExternalTag
    let isInternalTag = has JsonUnionEncoding.InternalTag

    let isUntagged =
        has JsonUnionEncoding.Untagged
        && not isAdjacentTag
        && not isExternalTag
        && not isInternalTag

    let namedFields = has JsonUnionEncoding.NamedFields

    let referenceId (context: OpenApiSchemaTransformerContext) (t: Type) =
        let openApiOptions =
            context.ApplicationServices.GetRequiredService<IOptionsMonitor<OpenApiOptions>>().Get(context.DocumentName)

        let typeInfo = context.JsonTypeInfo.Options.GetTypeInfo t

        match openApiOptions.CreateSchemaReferenceId.Invoke typeInfo with
        | null -> None
        | id when id.Length = 0 -> None
        | id -> Some id

    let schemaFor (context: OpenApiSchemaTransformerContext) (cancellationToken: CancellationToken) (t: Type) =
        let active =
            building.Value
            |> Option.ofObj
            |> Option.defaultValue ImmutableHashSet<Type>.Empty

        if active.Contains t then
            match referenceId context t with
            | Some id -> Task.FromResult(Schema.componentPlaceholder id)
            | None -> invalidOp $"Recursive type '%O{t}' must be described by a named schema component."
        else
            task {
                let! schema = context.GetOrCreateSchemaAsync(t, null, cancellationToken)
                return schema :> IOpenApiSchema
            }

    let fieldSchemas
        (context: OpenApiSchemaTransformerContext)
        cancellationToken
        (name: PropertyInfo -> string)
        (fields: PropertyInfo seq)
        =
        task {
            let result = ResizeArray()

            for field in fields do
                let! schema = schemaFor context cancellationToken field.PropertyType
                result.Add((name field, schema, not (FSharpShape.isOptional field.PropertyType)))

            return List.ofSeq result
        }

    let recordSchema (context: OpenApiSchemaTransformerContext) cancellationToken (t: Type) =
        task {
            let serializerOptions = context.JsonTypeInfo.Options

            let fields =
                FSharpType.GetRecordFields(t, BindingFlags.Public ||| BindingFlags.NonPublic)
                |> Seq.filter (Naming.isIgnored >> not)

            let! properties =
                fieldSchemas context cancellationToken (Naming.recordField serializerOptions) fields

            return Schema.object properties
        }

    /// The JSON value FSharp.SystemTextJson writes for a case's fields, before the tag is applied.
    let casePayload (context: OpenApiSchemaTransformerContext) cancellationToken (case: UnionCaseInfo) =
        task {
            let serializerOptions = context.JsonTypeInfo.Options
            let fields = case.GetFields() |> List.ofArray
            let fieldName = Naming.unionField options serializerOptions case

            match fields with
            | [ field ] when
                namedFields
                && has JsonUnionEncoding.UnwrapRecordCases
                && FSharpType.IsRecord(field.PropertyType, true)
                ->
                return! schemaFor context cancellationToken field.PropertyType
            | [ field ] when not namedFields && has JsonUnionEncoding.UnwrapSingleFieldCases ->
                return! schemaFor context cancellationToken field.PropertyType
            | _ when namedFields ->
                let! properties = fieldSchemas context cancellationToken fieldName fields
                return Schema.object properties :> IOpenApiSchema
            | _ ->
                let! elements = fieldSchemas context cancellationToken fieldName fields
                return Schema.tuple (elements |> List.map (fun (_, schema, _) -> schema)) :> IOpenApiSchema
        }

    let caseSchema (context: OpenApiSchemaTransformerContext) cancellationToken (case: UnionCaseInfo) =
        task {
            let tag = Naming.unionTag options case
            let fields = case.GetFields()

            if fields.Length = 0 && has JsonUnionEncoding.UnwrapFieldlessTags then
                return Schema.constant tag
            elif isInternalTag && namedFields then
                let serializerOptions = context.JsonTypeInfo.Options

                let! properties =
                    fieldSchemas context cancellationToken (Naming.unionField options serializerOptions case) fields

                return Schema.object ((options.UnionTagName, Schema.constant tag, true) :: properties) :> IOpenApiSchema
            elif isInternalTag then
                let serializerOptions = context.JsonTypeInfo.Options

                let! elements =
                    fieldSchemas context cancellationToken (Naming.unionField options serializerOptions case) fields

                return
                    Schema.tuple (Schema.constant tag :: (elements |> List.map (fun (_, s, _) -> s))) :> IOpenApiSchema
            elif isExternalTag then
                let! payload = casePayload context cancellationToken case
                return Schema.object [ tag.ToString(), payload, true ] :> IOpenApiSchema
            elif isUntagged then
                return! casePayload context cancellationToken case
            elif fields.Length = 0 then
                return Schema.object [ options.UnionTagName, Schema.constant tag, true ] :> IOpenApiSchema
            else
                let! payload = casePayload context cancellationToken case

                return
                    Schema.object
                        [ options.UnionTagName, Schema.constant tag, true
                          options.UnionFieldsName, payload, true ]
                    :> IOpenApiSchema
        }

    let unionSchema (context: OpenApiSchemaTransformerContext) cancellationToken (t: Type) (target: OpenApiSchema) =
        task {
            let cases = FSharpType.GetUnionCases(t, true)

            match cases with
            | [| case |] when has JsonUnionEncoding.UnwrapSingleCaseUnions && case.GetFields().Length = 1 ->
                let! inner = schemaFor context cancellationToken (case.GetFields().[0].PropertyType)
                Schema.sameAs inner target
            | _ ->
                let schemas = ResizeArray<IOpenApiSchema>()

                for case in cases do
                    let! schema = caseSchema context cancellationToken case
                    schemas.Add schema

                // Option's None is null at runtime, so it serializes as null whatever the encoding.
                if t.IsGenericType && t.GetGenericTypeDefinition() = typedefof<obj option> then
                    schemas.Add(Schema.nullSchema ())

                // Untagged cases carry no tag, so one JSON value can fit several of them.
                if isUntagged then
                    target.AnyOf <- schemas
                else
                    target.OneOf <- schemas
        }

    /// Map keys written as JSON object property names: strings and single-case unions wrapping a string.
    let isStringKey (key: Type) =
        key = typeof<string>
        || (FSharpShape.classify key = FSharpShape.Union
            && has JsonUnionEncoding.UnwrapSingleCaseUnions
            && (match FSharpType.GetUnionCases(key, true) with
                | [| case |] ->
                    match case.GetFields() with
                    | [| field |] -> field.PropertyType = typeof<string>
                    | _ -> false
                | _ -> false))

    let transform (schema: OpenApiSchema) (context: OpenApiSchemaTransformerContext) cancellationToken =
        task {
            let t = context.JsonTypeInfo.Type
            let shape = FSharpShape.classify t

            if FSharpShape.isHandled options shape then
                match shape with
                | FSharpShape.Option inner
                | FSharpShape.ValueOption inner when has JsonUnionEncoding.UnwrapOption ->
                    let! innerSchema = schemaFor context cancellationToken inner
                    Schema.nullableOf innerSchema schema
                | FSharpShape.Skippable inner ->
                    let! innerSchema = schemaFor context cancellationToken inner
                    Schema.sameAs innerSchema schema
                | FSharpShape.List element
                | FSharpShape.Set element ->
                    let! items = schemaFor context cancellationToken element
                    Schema.copyInto (Schema.array items) schema
                | FSharpShape.Map(key, value) ->
                    let! valueSchema = schemaFor context cancellationToken value

                    let asObject =
                        match options.MapFormat with
                        | MapFormat.Object -> true
                        | MapFormat.ArrayOfPairs -> false
                        | _ -> isStringKey key

                    if asObject then
                        let map = OpenApiSchema(Type = Nullable JsonSchemaType.Object)
                        map.AdditionalProperties <- valueSchema
                        Schema.copyInto map schema
                    else
                        let! keySchema = schemaFor context cancellationToken key
                        Schema.copyInto (Schema.array (Schema.tuple [ keySchema; valueSchema ])) schema
                | FSharpShape.Tuple elements ->
                    let schemas = ResizeArray()

                    for element in elements do
                        let! elementSchema = schemaFor context cancellationToken element
                        schemas.Add elementSchema

                    Schema.copyInto (Schema.tuple (List.ofSeq schemas)) schema
                | FSharpShape.Record ->
                    let! record = recordSchema context cancellationToken t
                    Schema.copyInto record schema
                | FSharpShape.Option _
                | FSharpShape.ValueOption _
                | FSharpShape.Union -> do! unionSchema context cancellationToken t schema
                | FSharpShape.Other -> ()
        }

    interface IOpenApiSchemaTransformer with
        member _.TransformAsync(schema, context, cancellationToken) =
            // Set inside the task so the AsyncLocal change is scoped to this call and never leaks to the caller.
            task {
                // Validate before schemaFor can re-enter the generator. Reference-id callbacks run too late.
                JsonConfiguration.validate options context.JsonTypeInfo.Options
                let previous = building.Value

                let active =
                    previous |> Option.ofObj |> Option.defaultValue ImmutableHashSet<Type>.Empty

                building.Value <- active.Add context.JsonTypeInfo.Type

                try
                    do! transform schema context cancellationToken
                finally
                    building.Value <- previous
            }
