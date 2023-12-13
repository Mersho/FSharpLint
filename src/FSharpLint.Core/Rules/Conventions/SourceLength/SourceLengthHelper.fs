module FSharpLint.Rules.Helper.SourceLength

open System
open FSharpLint.Framework
open FSharpLint.Framework.Suggestion
open FSharp.Compiler.Text

[<RequireQualifiedAccess>]
type Config = { MaxLines:int }

let private error name lineCount actual =
    let errorFormatString = Resources.GetString("RulesSourceLengthError")
    String.Format(errorFormatString, name, lineCount, actual)

let private length (range:Range) = range.EndLine - range.StartLine

let checkSourceLengthRule (config:Config) range errorName =
    let actualLines = length range
    if actualLines > config.MaxLines then
        { Range = range
          Message = error errorName config.MaxLines actualLines
          SuggestedFix = None
          TypeChecks = [] } |> Array.singleton

    else
        Array.empty
