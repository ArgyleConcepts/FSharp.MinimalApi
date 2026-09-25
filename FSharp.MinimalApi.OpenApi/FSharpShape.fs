namespace FSharp.MinimalApi.OpenApi

open System
open System.Reflection
open System.Text.Json
open System.Text.Json.Nodes
open System.Text.Json.Serialization
open Microsoft.FSharp.Reflection

/// The F# type shapes that FSharp.SystemTextJson serializes with its own converters.
[<RequireQualifiedAccess; NoComparison>]
type internal FSharpShape =
    | Option of inner: Type
    | ValueOption of inner: Type
    | Skippable of inner: Type
    | List of element: Type
    | Set of element: Type
    | Map of key: Type * value: Type
    | Tuple of elements: Type array
    | Record
    | Union
    | Other

module internal FSharpShape =
    let private isGenericOf (definition: Type) (t: Type) =
        t.IsGenericType && t.GetGenericTypeDefinition() = definition

    let private firstArg (t: Type) = t.GetGenericArguments()[0]

    let classify (t: Type) =
        if isGenericOf typedefof<obj option> t then
            FSharpShape.Option(firstArg t)
        elif isGenericOf typedefof<obj voption> t then
            FSharpShape.ValueOption(firstArg t)
        elif isGenericOf typedefof<Skippable<obj>> t then
            FSharpShape.Skippable(firstArg t)
        elif isGenericOf typedefof<obj list> t then
            FSharpShape.List(firstArg t)
        elif isGenericOf typedefof<Set<string>> t then
            FSharpShape.Set(firstArg t)
        elif isGenericOf typedefof<Map<string, obj>> t then
            let arguments = t.GetGenericArguments()
            FSharpShape.Map(arguments[0], arguments[1])
        elif FSharpType.IsTuple t then
            FSharpShape.Tuple(FSharpType.GetTupleElements t)
        elif FSharpType.IsRecord(t, true) then
            FSharpShape.Record
        elif FSharpType.IsUnion(t, true) then
            FSharpShape.Union
        else
            FSharpShape.Other

    /// Structural types are described inline instead of as named components.
    let isInline (t: Type) =
        match classify t with
        | FSharpShape.Option _
        | FSharpShape.ValueOption _
        | FSharpShape.Skippable _
        | FSharpShape.List _
        | FSharpShape.Set _
        | FSharpShape.Map _
        | FSharpShape.Tuple _ -> true
        | FSharpShape.Record
        | FSharpShape.Union
        | FSharpShape.Other -> false

    /// Whether FSharp.SystemTextJson owns serialization of this shape under the given options.
    let isHandled (options: JsonFSharpOptions) (shape: FSharpShape) =
        let types = options.Types
        let has (flag: JsonFSharpTypes) = types.HasFlag flag

        match shape with
        | FSharpShape.Option _ -> has JsonFSharpTypes.Options
        | FSharpShape.ValueOption _ -> has JsonFSharpTypes.ValueOptions
        | FSharpShape.Skippable _ -> true
        | FSharpShape.List _ -> has JsonFSharpTypes.Lists
        | FSharpShape.Set _ -> has JsonFSharpTypes.Sets
        | FSharpShape.Map _ -> has JsonFSharpTypes.Maps
        | FSharpShape.Tuple _ -> has JsonFSharpTypes.Tuples
        | FSharpShape.Record -> has JsonFSharpTypes.Records
        | FSharpShape.Union -> has JsonFSharpTypes.Unions
        | FSharpShape.Other -> false

    /// Fields that FSharp.SystemTextJson may omit or write as null.
    let isOptional (t: Type) =
        match classify t with
        | FSharpShape.Option _
        | FSharpShape.ValueOption _
        | FSharpShape.Skippable _ -> true
        | _ -> false

module internal Naming =
    let private text (value: string) : JsonNode =
        match JsonValue.Create value with
        | null -> invalidArg (nameof value) "A JSON name must not be null."
        | node -> node

    let toNode (name: JsonName) : JsonNode =
        match name with
        | JsonName.String value -> text value
        | JsonName.Int value -> JsonValue.Create value
        | JsonName.Bool value -> JsonValue.Create value

    let private jsonNames (memberInfo: MemberInfo) =
        memberInfo.GetCustomAttributes<JsonNameAttribute>(true)

    let private convert (policy: JsonNamingPolicy | null) (name: string) =
        match policy with
        | null -> name
        | policy -> policy.ConvertName name

    let private jsonNameText (name: JsonName) =
        match name with
        | JsonName.String value -> value
        | JsonName.Int value -> string<int> value
        | JsonName.Bool value -> if value then "true" else "false"

    let isIgnored (property: PropertyInfo) =
        match property.GetCustomAttribute<JsonIgnoreAttribute>(true) with
        | null -> false
        | attribute -> attribute.Condition = JsonIgnoreCondition.Always

    /// Record field name: [<JsonName>], then [<JsonPropertyName>], then the serializer's naming policy.
    let recordField (serializerOptions: JsonSerializerOptions) (property: PropertyInfo) =
        match jsonNames property |> Seq.tryFind (fun a -> isNull (box a.Field)) with
        | Some attribute -> jsonNameText attribute.Name
        | None ->
            match property.GetCustomAttribute<JsonPropertyNameAttribute>(true) with
            | null -> convert serializerOptions.PropertyNamingPolicy property.Name
            | attribute -> attribute.Name

    /// Union case tag value: [<JsonName>] on the case, then the union tag naming policy.
    let unionTag (options: JsonFSharpOptions) (case: UnionCaseInfo) : JsonNode =
        match
            case.GetCustomAttributes(typeof<JsonNameAttribute>)
            |> Seq.cast<JsonNameAttribute>
            |> Seq.tryFind (fun a -> isNull (box a.Field))
        with
        | Some attribute -> toNode attribute.Name
        | None -> text (convert options.UnionTagNamingPolicy case.Name)

    let private isGeneratedFieldName (name: string) =
        name = "Item"
        || (name.StartsWith("Item", StringComparison.Ordinal)
            && name.Length > 4
            && Seq.forall Char.IsDigit (name.Substring 4))

    let private fieldJsonName (case: UnionCaseInfo) (field: PropertyInfo) =
        let fromCase =
            case.GetCustomAttributes(typeof<JsonNameAttribute>)
            |> Seq.cast<JsonNameAttribute>
            |> Seq.tryFind (fun a -> a.Field = field.Name)

        jsonNames field |> Seq.tryHead |> Option.orElse fromCase

    /// Union field name: [<JsonName>] on the field or case, then field names from types, then the naming policy.
    let unionField
        (options: JsonFSharpOptions)
        (serializerOptions: JsonSerializerOptions)
        (case: UnionCaseInfo)
        (field: PropertyInfo)
        =
        let encoding = options.UnionEncoding

        let namedFromType (field: PropertyInfo) =
            encoding.HasFlag JsonUnionEncoding.UnionFieldNamesFromTypes
            && isGeneratedFieldName field.Name
            && (fieldJsonName case field).IsNone

        match fieldJsonName case field with
        | Some attribute -> jsonNameText attribute.Name
        | None ->
            let name =
                if namedFromType field then
                    // Fields named after the same type are numbered from 1, like FSharp.SystemTextJson does.
                    let sameType =
                        case.GetFields()
                        |> Array.filter (fun f -> namedFromType f && f.PropertyType.Name = field.PropertyType.Name)

                    if sameType.Length > 1 then
                        let index = sameType |> Array.findIndex (fun f -> f.Name = field.Name)
                        $"%s{field.PropertyType.Name}%d{index + 1}"
                    else
                        field.PropertyType.Name
                else
                    field.Name

            // FSharp.SystemTextJson falls back to the serializer's naming policy when no union field policy is set.
            if isNull (box options.UnionFieldNamingPolicy) then
                convert serializerOptions.PropertyNamingPolicy name
            else
                options.UnionFieldNamingPolicy.ConvertName name
