module FSharpLint.Rules.AvoidDoubleLookupDictionaries

open FSharp.Compiler.Syntax
open FSharpLint.Framework.Ast
open FSharpLint.Framework.Rules
open FSharpLint.Framework
open FSharpLint.Framework.Suggestion

let runner (args: AstNodeRuleParams) =
    printfn "%A" args.AstNode
    failwith "not implemented"

let rule =
    {
        Name = "AvoidDoubleLookupDictionaries"
        Identifier = Identifiers.AvoidDoubleLookupDictionaries
        RuleConfig =
            {
                AstNodeRuleConfig.Runner = runner
                Cleanup = ignore
            }
    }
    |> AstNodeRule
