module FSharpLint.Rules.UnreachableAsyncException

open System
open FSharp.Compiler.Syntax
open FSharpLint.Framework.Ast
open FSharpLint.Framework.Rules
open FSharpLint.Framework
open FSharpLint.Framework.Suggestion

let runner (args: AstNodeRuleParams) =
    printfn "%A" args.AstNode
    failwith "Not implemented yet."

let rule =
    {
        Name = "UnreachableAsyncException"
        Identifier = Identifiers.UnreachableAsyncException
        RuleConfig =
            {
                AstNodeRuleConfig.Runner = runner
                Cleanup = ignore
            }
    }
    |> AstNodeRule
