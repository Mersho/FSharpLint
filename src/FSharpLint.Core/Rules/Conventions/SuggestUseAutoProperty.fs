﻿module FSharpLint.Rules.SuggestUseAutoProperty

open System
open FSharpLint.Framework
open FSharpLint.Framework.Suggestion
open FSharp.Compiler.Syntax
open FSharpLint.Framework.Ast
open FSharpLint.Framework.Rules

let private runner (args: AstNodeRuleParams) =
    match args.AstNode with
    | MemberDefinition(
        SynMemberDefn.Member(
            SynBinding(accessibility, kind, _, false, _attributes, _xmlDoc, valData, headPat, returnInfo, expr, bindingRange, _), memberRange)) ->
        match expr with
        | SynExpr.Const(constant, range) ->
            { Range = range
              Message = Resources.GetString "RulesSuggestUseAutoProperty"
              SuggestedFix = None
              TypeChecks = List.Empty }
            |> Array.singleton
        | SynExpr.Ident(ident) ->
            { Range = ident.idRange
              Message = Resources.GetString "RulesSuggestUseAutoProperty"
              SuggestedFix = None
              TypeChecks = List.Empty }
            |> Array.singleton
        | _ -> Array.empty
    | _ -> Array.empty

let rule =
    { Name = "SuggestUseAutoProperty"
      Identifier = Identifiers.FavourConsistentThis
      RuleConfig = { AstNodeRuleConfig.Runner = runner; Cleanup = ignore } }
    |> AstNodeRule
