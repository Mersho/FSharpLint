// --------------------------------------------------------------------------------------
// FAKE build script
// --------------------------------------------------------------------------------------
#r "paket: groupref build //"
#load ".fake/build.fsx/intellisense.fsx"

open Fake.Core
open Fake.DotNet
open Fake.Tools
open Fake.IO
open Fake.IO.FileSystemOperators
open Fake.IO.Globbing.Operators
open Fake.Core.TargetOperators
open Fake.Api

open System
open System.IO

Target.initEnvironment()

// --------------------------------------------------------------------------------------
// Information about the project to be used at NuGet and in AssemblyInfo files
// --------------------------------------------------------------------------------------

let project = "FSharpLint"

let authors = "Matthew Mcveigh"

let gitOwner = "fsprojects"
let gitName = "FSharpLint"
let gitHome = "https://github.com/" + gitOwner
let gitUrl = gitHome + "/" + gitName

// --------------------------------------------------------------------------------------
// Helpers
// --------------------------------------------------------------------------------------
let isNullOrWhiteSpace = System.String.IsNullOrWhiteSpace

let exec cmd args dir =
    let proc =
        CreateProcess.fromRawCommandLine cmd args
        |> CreateProcess.ensureExitCodeWithMessage (sprintf "Error while running '%s' with args: %s" cmd args)
    (if isNullOrWhiteSpace dir then proc
    else proc |> CreateProcess.withWorkingDirectory dir)
    |> Proc.run
    |> ignore

let getBuildParam var =
    let value = Environment.environVar var
    if String.IsNullOrWhiteSpace value then
        None
    else
        Some value
let DoNothing = ignore

// --------------------------------------------------------------------------------------
// Build variables
// --------------------------------------------------------------------------------------

let buildDir  = "./build/"
let nugetDir  = "./out/"
let rootDir = __SOURCE_DIRECTORY__ |> DirectoryInfo

System.Environment.CurrentDirectory <- rootDir.FullName
let changelogFilename = "CHANGELOG.md"
let changelog = Changelog.load changelogFilename

let githubRef = Environment.GetEnvironmentVariable "GITHUB_REF"
let tagPrefix = "refs/tags/"
let isTag =
    if isNull githubRef then
        false
    else
        githubRef.StartsWith tagPrefix

let nugetVersion =
    match (changelog.Unreleased, isTag) with
    | (Some _unreleased, true) -> failwith "Shouldn't publish a git tag for changes outside a real release"
    | (None, true) ->
        changelog.LatestEntry.NuGetVersion
    | (_, false) ->
        let current = changelog.LatestEntry.NuGetVersion |> SemVer.parse
        let bumped = { current with
                            Patch = current.Patch + 1u
                            Original = None
                            PreRelease = None }
        let bumpedBaseVersion = string bumped

        let nugetPreRelease = Path.Combine(rootDir.FullName, "nugetPreRelease.fsx")
        let procResult =
            CreateProcess.fromRawCommand
                "dotnet"
                [
                    "fsi"
                    nugetPreRelease
                    bumpedBaseVersion
                ]
            |> CreateProcess.redirectOutput
            |> CreateProcess.ensureExitCode
            |> Proc.run
        procResult.Result.Output.Trim()

let PackageReleaseNotes baseProps =
    if isTag then
        ("PackageReleaseNotes", sprintf "%s/blob/v%s/CHANGELOG.md" gitUrl nugetVersion)::baseProps
    else
        baseProps

// --------------------------------------------------------------------------------------
// Build Targets
// --------------------------------------------------------------------------------------

Target.create "Clean" (fun _ ->
    Shell.cleanDirs [buildDir; nugetDir]
)

Target.create "Build" (fun _ ->
    DotNet.build id "FSharpLint.sln"
)

let filterPerformanceTests (p:DotNet.TestOptions) = { p with Filter = Some "\"TestCategory!=Performance\""; Configuration = DotNet.Release }

Target.create "Test" (fun _ ->
  DotNet.test filterPerformanceTests "tests/FSharpLint.Core.Tests"
  DotNet.test filterPerformanceTests "tests/FSharpLint.Console.Tests"
  DotNet.restore id "tests/FSharpLint.FunctionalTest.TestedProject/FSharpLint.FunctionalTest.TestedProject.sln"
  DotNet.test filterPerformanceTests "tests/FSharpLint.FunctionalTest"
)

Target.create "Docs" (fun _ ->
    exec "dotnet"  @"fornax build" "docs"
)

// --------------------------------------------------------------------------------------
// Release Targets
// --------------------------------------------------------------------------------------

Target.create "BuildRelease" (fun _ ->
    let properties = ("Version", nugetVersion) |> List.singleton |> PackageReleaseNotes

    DotNet.build (fun p ->
        { p with
            Configuration = DotNet.BuildConfiguration.Release
            OutputPath = Some buildDir
            MSBuildParams = { p.MSBuildParams with Properties = properties }
        }
    ) "FSharpLint.sln"
)


Target.create "Pack" (fun _ ->
    let properties = PackageReleaseNotes ([
        ("Version", nugetVersion);
        ("Authors", authors)
        ("PackageProjectUrl", gitUrl)
        ("RepositoryType", "git")
        ("RepositoryUrl", gitUrl)
        ("PackageLicenseExpression", "MIT")
    ])

    DotNet.pack (fun p ->
        { p with
            Configuration = DotNet.BuildConfiguration.Release
            OutputPath = Some nugetDir
            MSBuildParams = { p.MSBuildParams with Properties = properties }
        }
    ) "FSharpLint.sln"
)

Target.create "Push" (fun _ ->
    let push key =
        Paket.push (fun p -> { p with WorkingDir = nugetDir; ApiKey = key; ToolType = ToolType.CreateLocalTool() })

    let key = getBuildParam "nuget-key"
    match getBuildParam "GITHUB_EVENT_NAME" with
    | None ->
        match key with
        | None ->
            let key = UserInput.getUserPassword "NuGet Key: "
            push key
        | Some key ->
            push key

    | Some "push" ->
        match key with
        | None ->
            Console.WriteLine "No nuget-key env var found, skipping..."
        | Some key ->
            if isTag then
                push key
            else
                match getBuildParam "GITHUB_SHA" with
                | None ->
                    failwith "GITHUB_SHA should have been populated"
                | Some commitHash ->
                    let gitArgs = sprintf "describe --exact-match --tags %s" commitHash
                    let proc =
                        CreateProcess.fromRawCommandLine "git" gitArgs
                        |> Proc.run
                    if proc.ExitCode <> 0 then
                        // commit is not a tag, so go ahead pushing a prerelease
                        push key
                    else
                        Console.WriteLine "Commit mapped to a tag, skipping pushing prerelease..."
    | _ ->
        Console.WriteLine "Github event name not 'push', skipping..."

)


Target.create "SelfCheck" (fun _ ->
    let runLinter () =
        let frameworkVersion = "net6.0"
        let srcDir = Path.Combine(rootDir.FullName, "src") |> DirectoryInfo

        let consoleProj = Path.Combine(srcDir.FullName, "FSharpLint.Console", "FSharpLint.Console.fsproj") |> FileInfo
        printfn "Checking %s..." consoleProj.FullName
        exec "dotnet" (sprintf "run --framework %s lint %s" frameworkVersion consoleProj.FullName) consoleProj.Directory.FullName

        let coreProj = Path.Combine(srcDir.FullName, "FSharpLint.Core", "FSharpLint.Core.fsproj") |> FileInfo
        printfn "Checking %s..." coreProj.FullName
        exec "dotnet" (sprintf "run --framework %s lint %s" frameworkVersion coreProj.FullName) consoleProj.Directory.FullName
    
    printfn "Run FsharpLint with defualt rules."
    runLinter ()

    let fsharplintJsonDir = Path.Combine("src", "FSharpLint.Core", "fsharplint.json")
    let fsharplintJsonText = File.ReadAllText fsharplintJsonDir
    let enableRecursiveAsyncFunction = fsharplintJsonText.Replace ("\"recursiveAsyncFunction\": { \"enabled\": false },",
                                                    "\"recursiveAsyncFunction\": { \"enabled\": true },")
    let enableNestedStatements =
        enableRecursiveAsyncFunction
            .Replace (
                "\"nestedStatements\": {\r\n        \"enabled\": false,",
                "\"nestedStatements\": {\r\n        \"enabled\": true,"
            )
    (* This rule is too complex and we can enable it later
    let enableCyclomaticComplexity =
        enableNestedStatements
            .Replace (
                "\"cyclomaticComplexity\": {\r\n        \"enabled\": false,",
                "\"cyclomaticComplexity\": {\r\n        \"enabled\": true,"
            )
    *)

    (* This rule must be improved and we can enable it later
    let enableAvoidSinglePipeOperator =
        enableNestedStatements
            .Replace (
                "\"avoidSinglePipeOperator\": { \"enabled\": false },",
                "\"avoidSinglePipeOperator\": { \"enabled\": true },")
    *)

    let enableMaxLinesInLambdaFunction =
        enableNestedStatements
            .Replace (
                "\"maxLinesInLambdaFunction\": {\r\n        \"enabled\": false,",
                "\"maxLinesInLambdaFunction\": {\r\n        \"enabled\": true,"
            )

    let enableMaxLinesInMatchLambdaFunction =
        enableMaxLinesInLambdaFunction
            .Replace (
                "\"maxLinesInMatchLambdaFunction\": {\r\n        \"enabled\": false,",
                "\"maxLinesInMatchLambdaFunction\": {\r\n        \"enabled\": true,"
            )
    
    let enableMaxLinesInValue =
        enableMaxLinesInMatchLambdaFunction
            .Replace (
                "\"maxLinesInValue\": {\r\n        \"enabled\": false,",
                "\"maxLinesInValue\": {\r\n        \"enabled\": true,"
            )
    
    let enablemaxLinesInFunction =
        enableMaxLinesInValue
            .Replace (
                "\"maxLinesInFunction\": {\r\n        \"enabled\": false,",
                "\"maxLinesInFunction\": {\r\n        \"enabled\": true,"
            )
    
    let enablemaxLinesInMember =
        enablemaxLinesInFunction
            .Replace (
                "\"maxLinesInMember\": {\r\n        \"enabled\": false,",
                "\"maxLinesInMember\": {\r\n        \"enabled\": true,"
            )

    let enablemaxLinesInConstructor =
        enablemaxLinesInMember
            .Replace (
                "\"maxLinesInConstructor\": {\r\n        \"enabled\": false,",
                "\"maxLinesInConstructor\": {\r\n        \"enabled\": true,"
            )

    let enablemaxLinesInProperty =
        enablemaxLinesInConstructor
            .Replace (
                "\"maxLinesInProperty\": {\r\n        \"enabled\": false,",
                "\"maxLinesInProperty\": {\r\n        \"enabled\": true,"
            )

    let enablemaxLinesInModule =
        enablemaxLinesInProperty
            .Replace (
                "\"maxLinesInModule\": {\r\n        \"enabled\": false,",
                "\"maxLinesInModule\": {\r\n        \"enabled\": true,"
            )

    let enablemaxLinesInRecord =
        enablemaxLinesInModule
            .Replace (
                "\"maxLinesInRecord\": {\r\n        \"enabled\": false,",
                "\"maxLinesInRecord\": {\r\n        \"enabled\": true,"
            )
    
    let enableMaxLinesInEnum =
        enablemaxLinesInRecord
            .Replace (
                "\"maxLinesInEnum\": {\r\n        \"enabled\": false,",
                "\"maxLinesInEnum\": {\r\n        \"enabled\": true,"
            )
    
    let enableMaxLinesInUnion =
        enableMaxLinesInEnum
            .Replace (
                "\"maxLinesInUnion\": {\r\n        \"enabled\": false,",
                "\"maxLinesInUnion\": {\r\n        \"enabled\": true,"
            )
    
    let enableMaxLinesInClass =
        enableMaxLinesInUnion
            .Replace (
                "\"maxLinesInClass\": {\r\n        \"enabled\": false,",
                "\"maxLinesInClass\": {\r\n        \"enabled\": true,"
            )
    
    let enableFavourTypedIgnore = 
        enableMaxLinesInClass
            .Replace (
                "\"favourTypedIgnore\": { \"enabled\": false },",
                "\"favourTypedIgnore\": { \"enabled\": true },"
            )

    let enableFavourStaticEmptyFields = 
        enableFavourTypedIgnore
            .Replace (
                "\"favourStaticEmptyFields\": { \"enabled\": false },",
                "\"favourStaticEmptyFields\": { \"enabled\": true },"
            )

    let enableFavourConsistentThis =
        enableFavourStaticEmptyFields
            .Replace (
                "\"favourConsistentThis\": {\r\n        \"enabled\": false,",
                "\"favourConsistentThis\": {\r\n        \"enabled\": true,"
            )
    
    let enableAvoidTooShortNames = 
        enableFavourConsistentThis
            .Replace (
                "\"avoidTooShortNames\": { \"enabled\": false },",
                "\"avoidTooShortNames\": { \"enabled\": true },"
            )

    let enableAsyncExceptionWithoutReturn = 
        enableAvoidTooShortNames
            .Replace (
                "\"asyncExceptionWithoutReturn\": { \"enabled\": false },",
                "\"asyncExceptionWithoutReturn\": { \"enabled\": true },"
            )

    let enableNoPartialFunctions =
        enableAsyncExceptionWithoutReturn
            .Replace (
                "\"noPartialFunctions\": {\r\n        \"enabled\": false,",
                "\"noPartialFunctions\": {\r\n        \"enabled\": true,"
            )
    
    let enableMaxNumberOfItemsInTuple =
        enableNoPartialFunctions
            .Replace (
                "\"maxNumberOfItemsInTuple\": {\r\n        \"enabled\": false,",
                "\"maxNumberOfItemsInTuple\": {\r\n        \"enabled\": true,"
            )

    let enableMaxNumberOfFunctionParameters =
        enableMaxNumberOfItemsInTuple
            .Replace (
                "\"maxNumberOfFunctionParameters\": {\r\n        \"enabled\": false,",
                "\"maxNumberOfFunctionParameters\": {\r\n        \"enabled\": true,"
            )

    File.WriteAllText (fsharplintJsonDir, enableMaxNumberOfFunctionParameters)

    printfn "Re-run FsharpLint and activate all rules."
    runLinter ()
)

// --------------------------------------------------------------------------------------
// Build order
// --------------------------------------------------------------------------------------
Target.create "Default" DoNothing
Target.create "Release" DoNothing

"Clean"
  ==> "Build"
  ==> "Test"
  ==> "Default"

"Clean"
 ==> "BuildRelease"
 ==> "Docs"

"Default"
  ==> "Pack"
  ==> "Push"
  ==> "Release"

Target.runOrDefaultWithArguments "Default"
