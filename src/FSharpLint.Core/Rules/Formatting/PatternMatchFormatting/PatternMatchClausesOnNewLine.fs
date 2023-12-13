module FSharpLint.Rules.PatternMatchClausesOnNewLine

open System
open FSharp.Compiler.Syntax
open FSharpLint.Framework
open FSharpLint.Framework.Suggestion
open FSharpLint.Framework.Ast
open FSharpLint.Framework.Rules
open FSharpLint.Rules.Helper

let check args _ (clauses:SynMatchClause list) _ =
    let choose (clauseOne: SynMatchClause) (clauseTwo: SynMatchClause) = 
        if clauseOne.Range.EndLine = clauseTwo.Range.StartLine then
            {
                Range = clauseTwo.Range
                Message = Resources.GetString("RulesFormattingPatternMatchClausesOnNewLineError")
                SuggestedFix = None
                TypeChecks = List.Empty
            }
            |> Some
        else
            None

    clauses
    |> List.toArray
    |> Array.pairwise
    |> Array.choose (fun (clauseOne, clauseTwo) -> choose clauseOne clauseTwo)

let runner (args:AstNodeRuleParams) = PatternMatchFormatting.isActualPatternMatch args check

let rule =
    { Name = "PatternMatchClausesOnNewLine"
      Identifier = Identifiers.PatternMatchClausesOnNewLine
      RuleConfig = { AstNodeRuleConfig.Runner = runner; Cleanup = ignore } }
    |> AstNodeRule
