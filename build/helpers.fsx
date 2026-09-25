#r "nuget: Fake.DotNet.Cli"
#r "nuget: Fake.IO.FileSystem"
#r "nuget: Fake.Core.CommandLineParsing"
#r "nuget: Fake.Testing.ReportGenerator"
#r "nuget: Fake.Core.Target"
#r "nuget: CommandLineParser.FSharp, 2.9.1"

open System
open System.Globalization
open System.Xml.Linq
open Fake.Core
open Fake.DotNet
open Fake.IO
open System.Net.Http
open Fake.Testing
open CommandLine

let (/) path1 path2 = Path.combine path1 path2

let solutionDir = __SOURCE_DIRECTORY__ / ".." |> Path.getFullName

let testReportFolder = solutionDir / "TestReport"

let args = Environment.GetCommandLineArgs()[2..]
let argParser = Parser.Default

let target name action = Target.create name action
let inline run (action: unit -> ^a) = (fun _ -> action ())
let target' name action = Target.create name (run action)

let targetArgs<'TOption> name action =
    Target.create name (fun ctx ->
        let result = argParser.ParseArguments<'TOption> ctx.Context.Arguments

        match result with
        | :? Parsed<'TOption> as parsed -> action parsed.Value
        | :? NotParsed<'TOption> as notParsed ->
            notParsed.Errors
            |> Seq.map string
            |> Seq.distinct
            |> String.concat Environment.NewLine
            |> Trace.traceError

            failwith "CommandLine Parse Error"

        | _ -> Trace.log "Skipping target")

let targetAsync name (action: _ -> Async<_>) =
    target name (action >> Async.Ignore >> Async.RunSynchronously)

let help = Target.description

let httpClient = lazy (new HttpClient())

let initEnviroment () =
    Environment.SetEnvironmentVariable("DOTNET_CLI_UI_LANGUAGE", "en-US")

    args
    |> Array.toList
    |> Context.FakeExecutionContext.Create false __SOURCE_FILE__
    |> Context.RuntimeContext.Fake
    |> Context.setExecutionContext

    help "list FAKE actions"
    target "Help" <| fun _ -> Target.listAvailable ()

    help "same as help"

    target "--list" <| fun _ -> Target.listAvailable ()

    Target.createFinal "Dispose" (fun _ ->
        if httpClient.IsValueCreated then
            httpClient.Value.Dispose())

    Target.activateFinal "Dispose"

let runWithDefaultTarget target =
    let ctx = Target.WithContext.runOrDefaultWithArguments target
    Target.updateBuildStatus ctx

let printProj (proj: string) =
    proj |> System.IO.Path.GetFileName |> Trace.traceHeader

let printFiles onde files =
    Trace.traceHeader $"Arquivos encontrados para {onde}:"

    files |> Seq.map (Path.toRelativeFrom solutionDir) |> Trace.logItems "✔️"

    files

/// Minimum line coverage for each library assembly; the test target fails below it.
let coverageThreshold = 0.9

let coveredAssemblies =
    [ "FSharp.MinimalApi"
      "FSharp.MinimalApi.Interop"
      "FSharp.MinimalApi.OpenApi" ]

let coverageFile (proj: string) =
    testReportFolder / $"{IO.Path.GetFileNameWithoutExtension proj}.cobertura.xml"

// Runs a Debug build: in Release the F# optimizer inlines small library members into the tests,
// so their own bodies never run and coverage is under-reported.
let dotnetTest proj =
    printProj proj

    let result =
        DotNet.exec
            id
            "test"
            $"--project \"{proj}\" --configuration Debug -- --coverage --coverage-output-format cobertura --coverage-output \"{coverageFile proj}\""

    if not result.OK then
        failwith $"Tests failed in {proj}"

let lineRates (file: string) =
    XDocument.Load(file).Descendants("package")
    |> Seq.map (fun p ->
        p.Attribute("name").Value, Double.Parse(p.Attribute("line-rate").Value, CultureInfo.InvariantCulture))

let checkCoverage (projects: string seq) =
    let rates =
        projects
        |> Seq.map coverageFile
        |> Seq.collect lineRates
        |> Seq.groupBy fst
        |> Seq.map (fun (name, rates) -> name, rates |> Seq.map snd |> Seq.max)
        |> Map.ofSeq

    for name in coveredAssemblies do
        match rates.TryFind name with
        | Some rate -> Trace.logfn "%s line coverage: %.1f%%" name (rate * 100.)
        | None -> ()

    let failures =
        coveredAssemblies
        |> List.choose (fun name ->
            match rates.TryFind name with
            | Some rate when rate >= coverageThreshold -> None
            | Some rate -> Some $"{name} has {rate * 100.:F1}%% line coverage"
            | None -> Some $"{name} has no coverage data")

    if not failures.IsEmpty then
        failures
        |> String.concat Environment.NewLine
        |> failwithf "Coverage is below %.0f%%:%s%s" (coverageThreshold * 100.) Environment.NewLine

let generateCoverageReport () =
    Trace.logfn "%s" testReportFolder
    Directory.ensure testReportFolder

    ReportGenerator.generateReports
        (fun p ->
            { p with
                TargetDir = testReportFolder
                LogVerbosity = ReportGenerator.LogVerbosity.Error
                ToolType = ToolType.CreateLocalTool()
                ReportTypes = [ ReportGenerator.ReportType.Html; ReportGenerator.ReportType.JsonSummary ] })
        [ $"{testReportFolder}/*.cobertura.xml" ]


let fantomasCheck () =
    let result = DotNet.exec id "fantomas" "check ."

    if result.ExitCode <> 0 then
        failwith "Some files need formatting, check output for more info"

let fantomasFormat () =
    let result = DotNet.exec id "fantomas" "."

    if not result.OK then
        printfn "Errors while formatting all files: %A" result.Messages

let updateLocalTools () =
    let result = DotNet.exec (fun o -> { o with RedirectOutput = true }) "tool" "list"

    if not result.OK then
        printfn "Update error: %A" result.Errors

    result.Results
    |> List.map (fun x -> x.Message)
    |> List.skip 2
    |> List.map ((String.splitStr " ") >> List.head)
    |> List.iter (fun tool -> DotNet.exec id "tool" $"update {tool}" |> ignore)
