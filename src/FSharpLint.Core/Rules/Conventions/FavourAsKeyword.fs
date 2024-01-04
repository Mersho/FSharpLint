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
        Array.concat
            (clauses
            |> List.map (fun clause ->
                match clause with
                | SynMatchClause(SynPat.Named(_, ident, _, _, _), Some(whenExpr), resultExpr, range, _) ->
                    match whenExpr with
                    | SynExpr.App(_, _, SynExpr.App(_, _, _, SynExpr.Ident conditionIdent, _), argExpr, _) when
                        conditionIdent.idText = ident.idText
                        ->
                        match argExpr with
                        | SynExpr.Const(_, constRange) ->
                            let constText = ExpressionUtilities.tryFindTextOfRange constRange args.FileContent
                            let matchText = ExpressionUtilities.tryFindTextOfRange range args.FileContent
                            let resultExprText = ExpressionUtilities.tryFindTextOfRange resultExpr.Range args.FileContent

                            let suggestedFix =
                                lazy
                                    (match constText, matchText, resultExprText with
                                     | Some cText, Some mText, Some rExprText ->
                                         let toText = sprintf "%s as %s -> %s" (cText) (ident.idText) (rExprText)

                                         Some(
                                             { FromText = mText
                                               FromRange = range
                                               ToText = toText }
                                         )
                                     | _ -> None)

                            { Range = range
                              Message = Resources.GetString "RulesFavourAsKeyword"
                              SuggestedFix = Some(suggestedFix)
                              TypeChecks = list.Empty }
                            |> Array.singleton
                        | _ -> Array.empty
                    | _ -> Array.empty
                | _ -> Array.empty)
            )

    | _ -> Array.empty

let rule =
    { Name = "FavourAsKeyword"
      Identifier = Identifiers.FavourAsKeyword
      RuleConfig =
        { AstNodeRuleConfig.Runner = runner
          Cleanup = ignore } }
    |> AstNodeRule
