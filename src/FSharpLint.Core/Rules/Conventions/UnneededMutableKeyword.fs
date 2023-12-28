module FSharpLint.Rules.UnneededMutableKeyword

open System
open FSharp.Compiler.Syntax
open FSharpLint.Framework.Ast
open FSharpLint.Framework.Rules
open FSharpLint.Framework
open FSharpLint.Framework.Suggestion

let runner (args: AstNodeRuleParams) =
    match args.AstNode, args.CheckInfo with
    | AstNode.ModuleDeclaration (SynModuleDecl.Let (_, bindings, _)), Some checkInfo ->
        match bindings with
        | SynBinding (_, _, _, isMutable, _, _, _, SynPat.Named (_, ident, _, _, varRange), _, _, _, _) :: _ when
            isMutable
            ->
            let symbolUses = checkInfo.GetAllUsesOfAllSymbolsInFile()
            let varName = ident.idText

            let varNameUsages =
                symbolUses
                |> Seq.filter (fun usage -> usage.Symbol.DisplayName = varName)

            if (varNameUsages |> Seq.length) <= 1 then
                { Range = varRange
                  Message = String.Format(Resources.GetString "RulesUnneededMutableKeyword", varName)
                  SuggestedFix = None
                  TypeChecks = list.Empty }
                |> Array.singleton
            else
                Array.empty
        | _ -> Array.empty
    | _ -> Array.empty

let rule =
    { Name = "UnneededMutableKeyword"
      Identifier = Identifiers.UnneededMutableKeyword
      RuleConfig =
        { AstNodeRuleConfig.Runner = runner
          Cleanup = ignore } }
    |> AstNodeRule
