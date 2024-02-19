#r "nuget: Fake.DotNet.Paket"
#r "nuget: Fake.DotNet.Cli"
#load "common.fsx"
#load "clean.fsx"

open Fake.DotNet

open Common

let properties = ("Version", nugetVersion) |> List.singleton |> PackageReleaseNotes

Paket.restore (fun paketRestoreParam ->
    { paketRestoreParam with
        WorkingDir = Common.rootDir.FullName
        ToolType = ToolType.CreateLocalTool()
    })

DotNet.build (fun p ->
    { p with
        Configuration = DotNet.BuildConfiguration.Release
        OutputPath = Some buildDir
        MSBuildParams = { p.MSBuildParams with Properties = properties }
    }
) "FSharpLint.sln"
