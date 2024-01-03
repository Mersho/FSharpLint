module FSharpLint.Rules.FavourAsKeyword

open System
open FSharp.Compiler.Syntax
open FSharpLint.Framework.Ast
open FSharpLint.Framework.Rules
open FSharpLint.Framework
open FSharpLint.Framework.Suggestion

let runner (args: AstNodeRuleParams) =
    printfn "%A" args.AstNode
    failwith "not implemented"

let rule =
    { Name = "FavourAsKeyword"
      Identifier = Identifiers.FavourAsKeyword
      RuleConfig =
        { AstNodeRuleConfig.Runner = runner
          Cleanup = ignore } }
    |> AstNodeRule
