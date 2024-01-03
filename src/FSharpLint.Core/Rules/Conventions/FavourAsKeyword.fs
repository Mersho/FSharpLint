module FSharpLint.Rules.FavourAsKeyword

open System
open FSharp.Compiler.Syntax
open FSharpLint.Framework.Ast
open FSharpLint.Framework.Rules
open FSharpLint.Framework
open FSharpLint.Framework.Suggestion

let runner (args: AstNodeRuleParams) =
    match args.AstNode with
    | AstNode.Expression(SynExpr.Match(_, _, clauses, _)) ->
        let wrongMatchExpressions =
            clauses
            |> List.filter (fun clause ->
                match clause with
                | SynMatchClause(SynPat.Named(_, ident, _, _, _), Some(whenExpr), _, _, _) ->
                    match whenExpr with
                    | SynExpr.App(_, _, SynExpr.App(_, _, _, SynExpr.Ident conditionIdent, _), _, _) ->
                        conditionIdent.idText = ident.idText
                    | _ -> false
                | _ -> false)

        if wrongMatchExpressions.Length > 0 then
            { Range = wrongMatchExpressions.Head.Range
              Message = Resources.GetString "RulesFavourAsKeyword"
              SuggestedFix = None
              TypeChecks = list.Empty }
            |> Array.singleton
        else
            Array.empty

    | _ -> Array.empty

let rule =
    { Name = "FavourAsKeyword"
      Identifier = Identifiers.FavourAsKeyword
      RuleConfig =
        { AstNodeRuleConfig.Runner = runner
          Cleanup = ignore } }
    |> AstNodeRule
