module FSharpLint.Rules.UnneededMutableKeyword

open System
open FSharp.Compiler.Syntax
open FSharpLint.Framework.Ast
open FSharpLint.Framework.Rules
open FSharpLint.Framework
open FSharpLint.Framework.Suggestion

let runner (args: AstNodeRuleParams) =
    match args.AstNode with
    | AstNode.ModuleDeclaration(SynModuleDecl.Let(_, bindings, letRange)) ->
        match bindings with
        | SynBinding (_, _, _, isMutable, _, _, _, SynPat.Named (_, ident, _, _, _), _, _, _, _) :: _ when
            isMutable
            ->
            let varName = ident.idText

            let findAllAssignments =
                args.SyntaxArray
                |> Array.filter (fun node ->
                    match node.Actual with
                    | AstNode.ModuleDeclaration(SynModuleDecl.DoExpr(_,
                                                                     SynExpr.LongIdentSet(longIdentWithDots, _, _),
                                                                     _)) ->
                        ExpressionUtilities.longIdentWithDotsToString longIdentWithDots = varName
                    | _ -> false)

            if findAllAssignments.Length < 1 then
                let suggestedFix =
                    lazy
                        (ExpressionUtilities.tryFindTextOfRange letRange args.FileContent
                         |> Option.map (fun fromText ->
                             { FromText = fromText
                               FromRange = letRange
                               ToText = fromText.Replace(" mutable", "") }))

                { Range = letRange
                  Message = String.Format(Resources.GetString "RulesUnneededMutableKeyword", varName)
                  SuggestedFix = Some(suggestedFix)
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
