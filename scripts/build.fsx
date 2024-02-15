#r "nuget: Fake.DotNet.Cli"
#r "nuget: Fake.DotNet.Paket"
#load "clean.fsx"

open Fake.DotNet

Paket.restore (fun paketRestoreParam ->
    { paketRestoreParam with
        WorkingDir = Common.rootDir.FullName
        ToolType = ToolType.CreateLocalTool()
    })

DotNet.build id "FSharpLint.sln"
