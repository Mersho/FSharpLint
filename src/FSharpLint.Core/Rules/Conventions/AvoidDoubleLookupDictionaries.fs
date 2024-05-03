module FSharpLint.Rules.AvoidDoubleLookupDictionaries

open FSharp.Compiler.Syntax
open FSharpLint.Framework.Ast
open FSharpLint.Framework.Rules
open FSharpLint.Framework
open FSharpLint.Framework.Suggestion

let runner (args: AstNodeRuleParams) =
    match args.AstNode with
    | AstNode.Expression(SynExpr.Match(_, expr, _, range, _)) ->
        match expr with
        | SynExpr.App(_, _, SynExpr.LongIdent(_, synLongIdent, _, _), _, _) ->
            if (synLongIdent.LongIdent |> List.last).idText = "ContainsKey" then
                let dictVar = synLongIdent.LongIdent |> List.head

                let isDictFound =
                    ExpressionUtilities.tryFindTextOfRange range args.FileContent
                    |> Option.map (fun text -> text.Contains(sprintf "%s.[" dictVar.idText))

                match isDictFound with
                | Some _ ->
                    {
                        Range = range
                        Message = Resources.GetString "RulesAvoidDoubleLookupDictionariesError"
                        SuggestedFix = None
                        TypeChecks = List.Empty
                    }
                    |> Array.singleton
                | None -> Array.empty
            else
                Array.empty
        | _ -> Array.empty
    | _ -> Array.empty

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
