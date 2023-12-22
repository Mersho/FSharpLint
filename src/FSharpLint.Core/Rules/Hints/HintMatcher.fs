module FSharpLint.Rules.HintMatcher

open System
open System.Collections.Generic
open System.Diagnostics
open FSharp.Compiler.Syntax
open FSharp.Compiler.Symbols
open FSharp.Compiler.CodeAnalysis
open FSharpLint.Framework
open FSharpLint.Framework.Suggestion
open FSharpLint.Framework.Ast
open FSharpLint.Framework.ExpressionUtilities
open FSharpLint.Framework.HintParser
open FSharpLint.Framework.Rules

type ToStringConfig = { Replace: bool
                        ParentAstNode: AstNode option
                        Args: AstNodeRuleParams
                        MatchedVariables: Dictionary<char, SynExpr>
                        ParentHintNode: option<HintNode>
                        HintNode: HintNode }

type Config =
    { HintTrie:MergeSyntaxTrees.Edges }

let rec private extractSimplePatterns = function
    | SynSimplePats.SimplePats(simplePatterns, _) ->
        simplePatterns
    | SynSimplePats.Typed(simplePatterns, _, _) ->
        extractSimplePatterns simplePatterns

let rec private extractIdent = function
    | SynSimplePat.Id(ident, _, isCompilerGenerated, _, _, _) -> (ident, isCompilerGenerated)
    | SynSimplePat.Attrib(simplePattern, _, _)
    | SynSimplePat.Typed(simplePattern, _, _) -> extractIdent simplePattern

[<RequireQualifiedAccess>]
type private LambdaArgumentMatch =
    | Variable of variable:char * identifier:string
    | Wildcard
    | NoMatch

let private matchLambdaArgument (LambdaArg.LambdaArg(hintArg), actualArg) =
    match extractSimplePatterns actualArg with
    | [] -> LambdaArgumentMatch.NoMatch
    | simplePattern::_ ->
        let identifier, isCompilerGenerated = extractIdent simplePattern

        let isWildcard = isCompilerGenerated && identifier.idText.StartsWith("_")

        match hintArg with
        | Expression.LambdaArg(Expression.Variable(variable)) when not isWildcard ->
            LambdaArgumentMatch.Variable(variable, identifier.idText)
        | Expression.LambdaArg(Expression.Wildcard) -> LambdaArgumentMatch.Wildcard
        | _ -> LambdaArgumentMatch.NoMatch

[<RequireQualifiedAccess>]
type private LambdaMatch =
    | Match of Map<char, string>
    | NoMatch

let private matchLambdaArguments (hintArgs:HintParser.LambdaArg list) (actualArgs:SynSimplePats list) =
    if List.length hintArgs <> List.length actualArgs then
        LambdaMatch.NoMatch
    else
        let matches =
            List.zip hintArgs actualArgs
            |> List.map matchLambdaArgument

        let allArgsMatch =
            matches
            |> List.forall (function
                | LambdaArgumentMatch.NoMatch -> false
                | _ -> true)

        if allArgsMatch then
            matches
            |> List.choose (function
                | LambdaArgumentMatch.Variable(variable, ident) -> Some(variable, ident)
                | _ -> None)
            |> Map.ofList
            |> LambdaMatch.Match
        else
            LambdaMatch.NoMatch

/// Converts a SynConst (FSharp AST) into a Constant (hint AST).
let private matchConst = function
    | SynConst.Bool value -> Some(Constant.Bool value)
    | SynConst.Int16 value  -> Some(Constant.Int16 value)
    | SynConst.Int32 value  -> Some(Constant.Int32 value)
    | SynConst.Int64 value  -> Some(Constant.Int64 value)
    | SynConst.UInt16 value  -> Some(Constant.UInt16 value)
    | SynConst.UInt32 value -> Some(Constant.UInt32 value)
    | SynConst.UInt64 value  -> Some(Constant.UInt64 value)
    | SynConst.Byte value  -> Some(Constant.Byte value)
    | SynConst.Bytes (value, _, _)  -> Some(Constant.Bytes value)
    | SynConst.Char value  -> Some(Constant.Char value)
    | SynConst.Decimal value  -> Some(Constant.Decimal value)
    | SynConst.Double value  -> Some(Constant.Double value)
    | SynConst.SByte value  -> Some(Constant.SByte value)
    | SynConst.Single value  -> Some(Constant.Single value)
    | SynConst.String (value, _, _)  -> Some(Constant.String value)
    | SynConst.UIntPtr value  -> Some(Constant.UIntPtr(unativeint value))
    | SynConst.IntPtr value  -> Some(Constant.IntPtr(nativeint value))
    | SynConst.UserNum (value, endChar)  ->
        Some(Constant.UserNum(System.Numerics.BigInteger.Parse(value), endChar.[0]))
    | SynConst.Unit -> Some Constant.Unit 
    | SynConst.UInt16s _ 
    | SynConst.SourceIdentifier _
    | SynConst.Measure _  -> None

module private Precedence =
    let private ofHint hint =
        match hint with
        | HintExpr(expr) ->
            match expr with
            | Expression.Lambda(_) -> Some 3
            | Expression.If(_) -> Some 2
            | Expression.AddressOf(_) | Expression.PrefixOperator(_) | Expression.InfixOperator(_)
            | Expression.FunctionApplication(_) -> Some 1
            | _ -> None
        | HintPat(_) -> None

    let private ofExpr expr =
        match expr with
        | SynExpr.Lambda(_) | SynExpr.MatchLambda(_) | SynExpr.Match(_)
        | SynExpr.TryFinally(_) | SynExpr.TryWith(_) -> Some 3
        | SynExpr.IfThenElse(_) -> Some 2
        | SynExpr.InferredDowncast(_) | SynExpr.InferredUpcast(_) | SynExpr.Assert(_) | SynExpr.Fixed(_)
        | SynExpr.Lazy(_) | SynExpr.New(_) | SynExpr.Tuple(true (* true = struct tuple  *) , _, _, _)
        | SynExpr.Downcast(_) | SynExpr.Upcast(_) | SynExpr.TypeTest(_) | SynExpr.AddressOf(_)
        | SynExpr.App(_) -> Some 1
        | _ -> None

    let requiresParenthesis (matchedVariables:Dictionary<_, _>) hintNode parentAstNode parentHintNode =
        let parentPrecedence =
            match parentHintNode with
            | Some(hint) -> ofHint hint
            | None ->
                match parentAstNode with
                | Some(AstNode.Expression(expr)) -> ofExpr expr
                | Some(_) | None -> None

        let hintPrecedence =
            match hintNode with
            | HintExpr(Expression.Variable(varChar)) ->
                match matchedVariables.TryGetValue varChar with
                | true, expr -> ofExpr expr
                | _ -> None
            | hint -> ofHint hint

        match (hintPrecedence, parentPrecedence) with
        | Some hint, Some parent -> hint >= parent
        | _ -> false

let private filterParens astNodes =
    let isNotParen = function
        | AstNode.Expression(SynExpr.Paren(_)) -> false
        | _ -> true

    List.filter isNotParen astNodes

module private MatchExpression =

    /// Extracts an expression from parentheses e.g. ((x + 4)) -> x + 4
    let rec private removeParens = function
        | AstNode.Expression(SynExpr.Paren(expr, _, _, _)) -> expr |> AstNode.Expression |> removeParens
        | node -> node

    [<NoEquality; NoComparison>]
    type Arguments =
        { LambdaArguments:Map<char, string>
          MatchedVariables:Dictionary<char, SynExpr>
          Expression:AstNode
          Hint:Expression
          FSharpCheckFileResults:FSharpCheckFileResults option
          Breadcrumbs:AstNode list }

        with
            member this.SubHint(expr, hint) =
                { this with Expression = expr; Hint = hint }

    let private matchExpr = function
        | AstNode.Expression(SynExpr.Ident(ident)) ->
            let ident = identAsDecompiledOpName ident
            Some(Expression.Identifier([ident]))
        | AstNode.Expression(SynExpr.LongIdent(_, ident, _, _)) ->
            let identifier = ident.Lid |> List.map (fun ident -> ident.idText)
            Some(Expression.Identifier(identifier))
        | AstNode.Expression(SynExpr.Const(constant, _)) ->
            matchConst constant |> Option.map Expression.Constant
        | AstNode.Expression(SynExpr.Null(_)) ->
            Some(Expression.Null)
        | _ -> None

    let private (|PossiblyInMethod|PossiblyInConstructor|NotInMethod|) breadcrumbs =
        let (|PossiblyMethodCallOrConstructor|_|) = function
            | SynExpr.App(_, false, _, _, _) -> Some()
            | _ -> None

        match breadcrumbs with
        | AstNode.Expression(SynExpr.Tuple(_))::AstNode.TypeParameter(_)::AstNode.Expression(SynExpr.New(_))::_
        | AstNode.Expression(SynExpr.Tuple(_))::AstNode.Expression(SynExpr.New(_))::_
        | AstNode.TypeParameter(_)::AstNode.Expression(SynExpr.New(_))::_
        | AstNode.Expression(SynExpr.New(_))::_ ->
            PossiblyInConstructor
        | AstNode.Expression(PossiblyMethodCallOrConstructor)::_
        | AstNode.Expression(SynExpr.Tuple(_))::AstNode.Expression(PossiblyMethodCallOrConstructor)::_
        | AstNode.Expression(SynExpr.Tuple(_))::AstNode.TypeParameter(_)::AstNode.Expression(PossiblyMethodCallOrConstructor)::_ ->
            PossiblyInMethod
        | _ -> NotInMethod

    [<NoComparison; NoEquality>]
    type HintMatch =
        | Match of (unit -> bool) list
        | NoMatch

    let private (&&~) lhs rhs =
        match (lhs, rhs) with
        | Match(asyncLhs), Match(asyncRhs) -> Match(asyncLhs @ asyncRhs)
        | _ -> NoMatch

    /// Check that an infix equality operation is not actually the assignment of a value to a property in a constructor
    /// or a named parameter in a method call.
    let private notPropertyInitialisationOrNamedParameter arguments leftExpr opExpr = 
        match (leftExpr, opExpr) with
        | SynExpr.Ident(ident), SynExpr.Ident(opIdent) when opIdent.idText = "op_Equality" ->
            match arguments.FSharpCheckFileResults with
            | Some(checkFile) ->
                let checkSymbol () = 
                    let symbolUse =
                        checkFile.GetSymbolUseAtLocation(
                            ident.idRange.StartLine, ident.idRange.EndColumn, String.Empty, [ident.idText])

                    match symbolUse with
                    | Some(symbolUse) ->
                        match symbolUse.Symbol with
                        | :? FSharpParameter
                        | :? FSharpField -> false
                        | :? FSharpMemberOrFunctionOrValue as fSharpMemberOrFunctionOrValue -> not fSharpMemberOrFunctionOrValue.IsProperty
                        | _ -> true
                    | None -> true
                fun () -> checkSymbol()
                |> List.singleton
                |> Match
            | None ->
                /// Check if in `new` expr or function application (either could be a constructor).
                match filterParens arguments.Breadcrumbs with
                | PossiblyInMethod
                | PossiblyInConstructor -> NoMatch
                | _ -> Match(List.Empty)
        | _ -> Match(List.Empty)

    let rec matchHintExpr arguments =
        let expr = removeParens arguments.Expression
        let arguments = { arguments with Expression = expr }

        match arguments.Hint with
        | Expression.Variable(variable) when arguments.LambdaArguments |> Map.containsKey variable ->
            match expr with
            | AstNode.Expression(SynExpr.Ident(identifier))
                    when identifier.idText = arguments.LambdaArguments.[variable] ->
                Match(List.Empty)
            | _ -> NoMatch
        | Expression.Variable(var) ->
            match expr with
            | AstNode.Expression(expr) -> arguments.MatchedVariables.TryAdd(var, expr) |> ignore<bool>
            | _ -> ()
            Match(List.Empty)
        | Expression.Wildcard ->
            Match(List.Empty)
        | Expression.Null
        | Expression.Constant(_)
        | Expression.Identifier(_) ->
            if matchExpr expr = Some(arguments.Hint) then Match(List.Empty)
            else NoMatch
        | Expression.Parentheses(hint) ->
            arguments.SubHint(expr, hint) |> matchHintExpr
        | Expression.Tuple(_) ->
            matchTuple arguments
        | Expression.List(_) ->
            matchList arguments
        | Expression.Array(_) ->
            matchArray arguments
        | Expression.If(_) ->
            matchIf arguments
        | Expression.AddressOf(_) ->
            matchAddressOf arguments
        | Expression.PrefixOperator(_) ->
            matchPrefixOperation arguments
        | Expression.InfixOperator(_) ->
            matchInfixOperation arguments
        | Expression.FunctionApplication(_) ->
            matchFunctionApplication arguments
        | Expression.Lambda(_) -> matchLambda arguments
        | Expression.LambdaArg(_)
        | Expression.LambdaBody(_) -> NoMatch
        | Expression.Else(_) -> NoMatch

    and private matchFunctionApplication arguments =
        match (arguments.Expression, arguments.Hint) with
        | FuncApp(exprs, _), Expression.FunctionApplication(hintExprs) ->
            let expressions = exprs |> List.map AstNode.Expression
            doExpressionsMatch expressions hintExprs arguments
        | _ -> NoMatch

    and private doExpressionsMatch expressions hintExpressions (arguments:Arguments) =
        if List.length expressions = List.length hintExpressions then
            (expressions, hintExpressions)
            ||> List.map2 (fun expr hint -> arguments.SubHint(expr, hint) |> matchHintExpr)
            |> List.fold (&&~) (Match(List.Empty))
        else
            NoMatch

    and private matchIf arguments =
        match (arguments.Expression, arguments.Hint) with
        | (AstNode.Expression(SynExpr.IfThenElse(cond, expr, None, _, _, _, _)),
           Expression.If(hintCond, hintExpr, None)) ->
            arguments.SubHint(Expression cond, hintCond) |> matchHintExpr &&~
            (arguments.SubHint(Expression expr, hintExpr) |> matchHintExpr)
        | (AstNode.Expression(SynExpr.IfThenElse(cond, expr, Some(elseExpr), _, _, _, _)),
           Expression.If(hintCond, hintExpr, Some(Expression.Else(hintElseExpr)))) ->
            arguments.SubHint(Expression cond, hintCond) |> matchHintExpr &&~
            (arguments.SubHint(Expression expr, hintExpr) |> matchHintExpr) &&~
            (arguments.SubHint(Expression elseExpr, hintElseExpr) |> matchHintExpr)
        | _ -> NoMatch

    and matchLambda arguments =
        match (arguments.Expression, arguments.Hint) with
        | Lambda({ Arguments = args; Body = body }, _), Expression.Lambda(lambdaArgs, LambdaBody(Expression.LambdaBody(lambdaBody))) ->
            match matchLambdaArguments lambdaArgs args with
            | LambdaMatch.Match(lambdaArguments) ->
                matchHintExpr { arguments.SubHint(AstNode.Expression(body), lambdaBody) with LambdaArguments = lambdaArguments }
            | LambdaMatch.NoMatch -> NoMatch
        | _ -> NoMatch

    and private matchTuple arguments =
        match (arguments.Expression, arguments.Hint) with
        | AstNode.Expression(SynExpr.Tuple(_, expressions, _, _)), Expression.Tuple(hintExpressions) ->
            let expressions = List.map AstNode.Expression expressions
            doExpressionsMatch expressions hintExpressions arguments
        | _ -> NoMatch

    and private matchList arguments =
        match (arguments.Expression, arguments.Hint) with
        | AstNode.Expression(SynExpr.ArrayOrList(false, expressions, _)), Expression.List(hintExpressions) ->
            let expressions = List.map AstNode.Expression expressions
            doExpressionsMatch expressions hintExpressions arguments
        | AstNode.Expression(SynExpr.ArrayOrListOfSeqExpr(false, SynExpr.CompExpr(true, _, expression, _), _)), Expression.List([hintExpression]) ->
            arguments.SubHint(AstNode.Expression(expression), hintExpression) |> matchHintExpr
        | _ -> NoMatch

    and private matchArray arguments =
        match (arguments.Expression, arguments.Hint) with
        | AstNode.Expression(SynExpr.ArrayOrList(true, expressions, _)), Expression.Array(hintExpressions) ->
            let expressions = List.map AstNode.Expression expressions
            doExpressionsMatch expressions hintExpressions arguments
        | AstNode.Expression(SynExpr.ArrayOrListOfSeqExpr(true, SynExpr.CompExpr(true, _, expression, _), _)), Expression.Array([hintExpression]) ->
            arguments.SubHint(AstNode.Expression(expression), hintExpression) |> matchHintExpr
        | _ -> NoMatch

    and private matchInfixOperation arguments =
        match (arguments.Expression, arguments.Hint) with
        | (AstNode.Expression(SynExpr.App(_, true, (SynExpr.Ident(_) as opExpr), SynExpr.Tuple(_, [leftExpr; rightExpr], _, _), _)),
           Expression.InfixOperator(op, left, right)) ->
            arguments.SubHint(AstNode.Expression(opExpr), op) |> matchHintExpr &&~
            (arguments.SubHint(AstNode.Expression(rightExpr), right) |> matchHintExpr) &&~
            (arguments.SubHint(AstNode.Expression(leftExpr), left) |> matchHintExpr)
        | (AstNode.Expression(SynExpr.App(_, _, infixExpr, rightExpr, _)),
           Expression.InfixOperator(op, left, right)) ->

            match removeParens <| AstNode.Expression(infixExpr) with
            | AstNode.Expression(SynExpr.App(_, true, opExpr, leftExpr, _)) ->
                arguments.SubHint(AstNode.Expression(opExpr), op) |> matchHintExpr &&~
                (arguments.SubHint(AstNode.Expression(leftExpr), left) |> matchHintExpr) &&~
                (arguments.SubHint(AstNode.Expression(rightExpr), right) |> matchHintExpr) &&~
                notPropertyInitialisationOrNamedParameter arguments leftExpr opExpr
            | _ -> NoMatch
        | _ -> NoMatch

    and private matchPrefixOperation arguments =
        match (arguments.Expression, arguments.Hint) with
        | (AstNode.Expression(SynExpr.App(_, _, opExpr, rightExpr, _)),
           Expression.PrefixOperator(Expression.Identifier([op]), expr)) ->
            arguments.SubHint(AstNode.Expression(opExpr), Expression.Identifier([op])) |> matchHintExpr &&~
            (arguments.SubHint(AstNode.Expression(rightExpr), expr) |> matchHintExpr)
        | _ -> NoMatch

    and private matchAddressOf arguments =
        match (arguments.Expression, arguments.Hint) with
        | AstNode.Expression(SynExpr.AddressOf(synSingleAmp, addrExpr, _, _)), Expression.AddressOf(singleAmp, expr) when synSingleAmp = singleAmp ->
            arguments.SubHint(AstNode.Expression(addrExpr), expr) |> matchHintExpr
        | _ -> NoMatch

module private MatchPattern =

    let private matchPattern = function
        | SynPat.LongIdent(ident, _, _, _, _, _) ->
            let identifier = ident.Lid |> List.map (fun ident -> ident.idText)
            Some(Pattern.Identifier(identifier))
        | SynPat.Const(constant, _) ->
            matchConst constant |> Option.map Pattern.Constant
        | SynPat.Null(_) ->
            Some(Pattern.Null)
        | _ -> None

    /// Extracts a pattern from parentheses e.g. ((x)) -> x
    let rec private removeParens = function
        | SynPat.Paren(pattern, _) -> removeParens pattern
        | pat -> pat

    let rec matchHintPattern (pattern, hint) =
        let pattern = removeParens pattern

        match hint with
        | Pattern.Variable(_)
        | Pattern.Wildcard ->
            true
        | Pattern.Null
        | Pattern.Constant(_)
        | Pattern.Identifier(_) ->
            matchPattern pattern = Some(hint)
        | Pattern.Cons(_) ->
            matchConsPattern (pattern, hint)
        | Pattern.Or(_) ->
            matchOrPattern (pattern, hint)
        | Pattern.Parentheses(hint) ->
            matchHintPattern (pattern, hint)
        | Pattern.Tuple(_) ->
            matchTuple (pattern, hint)
        | Pattern.List(_) ->
            matchList (pattern, hint)
        | Pattern.Array(_) ->
            matchArray (pattern, hint)

    and private doPatternsMatch patterns hintExpressions =
        (List.length patterns = List.length hintExpressions)
        && (patterns, hintExpressions) ||> List.forall2 (fun pattern hint -> matchHintPattern (pattern, hint))

    and private matchList (pattern, hint) =
        match (pattern, hint) with
        | SynPat.ArrayOrList(false, patterns, _), Pattern.List(hintExpressions) ->
            doPatternsMatch patterns hintExpressions
        | _ -> false

    and private matchArray (pattern, hint) =
        match (pattern, hint) with
        | SynPat.ArrayOrList(true, patterns, _), Pattern.Array(hintExpressions) ->
            doPatternsMatch patterns hintExpressions
        | _ -> false

    and private matchTuple (pattern, hint) =
        match (pattern, hint) with
        | SynPat.Tuple(_, patterns, _), Pattern.Tuple(hintExpressions) ->
            doPatternsMatch patterns hintExpressions
        | _ -> false

    and private matchConsPattern (pattern, hint) =
        match (pattern, hint) with
        | SynPat.LongIdent(
                            LongIdentWithDots([ident],_),
                            _,
                            _,
                            SynArgPats.Pats([SynPat.Tuple(_, [leftPattern;rightPattern], _)]),
                            _,
                            _), Pattern.Cons(left, right)
                when ident.idText = "op_ColonColon" ->
            matchHintPattern (leftPattern, left) && matchHintPattern (rightPattern, right)
        | _ -> false

    and private matchOrPattern (pattern, hint) =
        match (pattern, hint) with
        | SynPat.Or(leftPattern, rightPattern, _), Pattern.Or(left, right) ->
            matchHintPattern (leftPattern, left) && matchHintPattern (rightPattern, right)
        | _ -> false

module private FormatHint =
    let private constantToString = function
        | Constant.Bool value -> if value then "true" else "false"
        | Constant.Int16 value -> value.ToString() + "s"
        | Constant.Int32 value -> value.ToString()
        | Constant.Int64 value -> value.ToString() + "L"
        | Constant.UInt16 value -> value.ToString() + "us"
        | Constant.UInt32 value -> value.ToString() + "u"
        | Constant.UInt64 value -> value.ToString() + "UL"
        | Constant.Byte value -> value.ToString() + "uy"
        | Constant.Bytes value -> value.ToString()
        | Constant.Char value -> "'" + value.ToString() + "'"
        | Constant.Decimal value -> value.ToString() + "m"
        | Constant.Double value -> value.ToString()
        | Constant.SByte value -> value.ToString() + "y"
        | Constant.Single value -> value.ToString() + "f"
        | Constant.String value -> "\"" + value + "\""
        | Constant.UIntPtr value -> value.ToString()
        | Constant.IntPtr value -> value.ToString()
        | Constant.UserNum(x, _) -> x.ToString()
        | Constant.Unit -> "()"

    let private surroundExpressionsString hintToString left right sep expressions =
        let inside =
            expressions
            |> List.map hintToString
            |> String.concat sep

        left + inside + right

    let private opToString = function
        | Expression.Identifier(identifier) -> String.concat "." identifier
        | expression ->
            Debug.Assert(false, "Expected operator to be an expression identifier, but was " + expression.ToString())
            String.Empty

    let rec toString (config: ToStringConfig) =
        let toString hintNode = toString { Replace = config.Replace
                                           ParentAstNode = config.ParentAstNode
                                           Args = config.Args
                                           MatchedVariables = config.MatchedVariables
                                           ParentHintNode = (Some config.HintNode)
                                           HintNode = hintNode}

        let str =
            match config.HintNode with
            | HintExpr(Expression.Variable(varChar)) when config.Replace ->
                match config.MatchedVariables.TryGetValue varChar with
                | true, expr ->
                    match ExpressionUtilities.tryFindTextOfRange expr.Range config.Args.FileContent with
                    | Some(replacement) -> replacement
                    | _ -> varChar.ToString()
                | _ -> varChar.ToString()
            | HintExpr(Expression.Variable(x))
            | HintPat(Pattern.Variable(x)) -> x.ToString()
            | HintExpr(Expression.Wildcard)
            | HintPat(Pattern.Wildcard) -> "_"
            | HintExpr(Expression.Constant(constant))
            | HintPat(Pattern.Constant(constant)) ->
                constantToString constant
            | HintExpr(Expression.Identifier(identifier))
            | HintPat(Pattern.Identifier(identifier)) ->
                identifier
                |> List.map PrettyNaming.DemangleOperatorName
                |> String.concat "."
            | HintExpr(Expression.FunctionApplication(expressions)) ->
                expressions |> surroundExpressionsString (HintExpr >> toString) String.Empty String.Empty " "
            | HintExpr(Expression.InfixOperator(operator, leftHint, rightHint)) ->
                toString (HintExpr leftHint) + " " + opToString operator + " " + toString (HintExpr rightHint)
            | HintPat(Pattern.Cons(leftHint, rightHint)) ->
                toString (HintPat leftHint) + "::" + toString (HintPat rightHint)
            | HintPat(Pattern.Or(leftHint, rightHint)) ->
                toString (HintPat leftHint) + " | " + toString (HintPat rightHint)
            | HintExpr(Expression.AddressOf(singleAmp, hint)) ->
                (if singleAmp then "&" else "&&") + toString (HintExpr hint)
            | HintExpr(Expression.PrefixOperator(operator, hint)) ->
                opToString operator + toString (HintExpr hint)
            | HintExpr(Expression.Parentheses(hint)) -> "(" + toString (HintExpr hint) + ")"
            | HintPat(Pattern.Parentheses(hint)) -> "(" + toString (HintPat hint) + ")"
            | HintExpr(Expression.Lambda(arguments, LambdaBody(body))) ->
                "fun "
                + lambdaArgumentsToString config.Replace config.ParentAstNode config.Args config.MatchedVariables arguments
                + " -> " + toString (HintExpr body)
            | HintExpr(Expression.LambdaArg(argument)) ->
                toString (HintExpr argument)
            | HintExpr(Expression.LambdaBody(body)) ->
                toString (HintExpr body)
            | HintExpr(Expression.Tuple(expressions)) ->
                expressions |> surroundExpressionsString (HintExpr >> toString) "(" ")" ","
            | HintExpr(Expression.List(expressions)) ->
                expressions |> surroundExpressionsString (HintExpr >> toString) "[" "]" ";"
            | HintExpr(Expression.Array(expressions)) ->
                expressions |> surroundExpressionsString (HintExpr >> toString) "[|" "|]" ";"
            | HintPat(Pattern.Tuple(expressions)) ->
                expressions |> surroundExpressionsString (HintPat >> toString) "(" ")" ","
            | HintPat(Pattern.List(expressions)) ->
                expressions |> surroundExpressionsString (HintPat >> toString) "[" "]" ";"
            | HintPat(Pattern.Array(expressions)) ->
                expressions |> surroundExpressionsString (HintPat >> toString) "[|" "|]" ";"
            | HintExpr(Expression.If(cond, expr, None)) ->
                "if " + toString (HintExpr cond) + " then " + toString (HintExpr expr)
            | HintExpr(Expression.If(cond, expr, Some(elseExpr))) ->
                "if " + toString (HintExpr cond) + " then " + toString (HintExpr expr) + " " + toString (HintExpr elseExpr)
            | HintExpr(Expression.Else(expr)) ->
                "else " + toString (HintExpr expr)
            | HintExpr(Expression.Null)
            | HintPat(Pattern.Null) -> "null"
        if config.Replace && Precedence.requiresParenthesis config.MatchedVariables config.HintNode config.ParentAstNode config.ParentHintNode then "(" + str + ")"
        else str
    and private lambdaArgumentsToString replace parentAstNode args matchedVariables (arguments:LambdaArg list) =
        arguments
        |> List.map (fun (LambdaArg expr) -> toString { Replace = replace
                                                        ParentAstNode = parentAstNode
                                                        Args = args
                                                        MatchedVariables = matchedVariables
                                                        ParentHintNode = None
                                                        HintNode = (HintExpr expr) } )
        |> String.concat " "

type HintErrorConfig = { TypeChecks: (unit -> bool) list
                         Hint: Hint
                         Args: AstNodeRuleParams
                         Range: FSharp.Compiler.Text.Range
                         MatchedVariables: Dictionary<char, SynExpr>
                         ParentAstNode: AstNode option }
let private hintError (config: HintErrorConfig) =
    let toStringConfig = { ToStringConfig.Replace = false
                           ToStringConfig.ParentAstNode = None
                           ToStringConfig.Args = config.Args
                           ToStringConfig.MatchedVariables = config.MatchedVariables
                           ToStringConfig.ParentHintNode = None
                           ToStringConfig.HintNode = config.Hint.MatchedNode }
    let matched = FormatHint.toString toStringConfig

    match config.Hint.Suggestion with
    | Suggestion.Expr(expr) ->
        let suggestion = FormatHint.toString { toStringConfig with HintNode = (HintExpr expr) }
        let errorFormatString = Resources.GetString("RulesHintRefactor")
        let error = System.String.Format(errorFormatString, matched, suggestion)

        let toText = FormatHint.toString { toStringConfig with 
                                               Replace = true
                                               ParentAstNode = config.ParentAstNode
                                               HintNode = (HintExpr expr) }

        let suggestedFix = lazy(
            ExpressionUtilities.tryFindTextOfRange config.Range config.Args.FileContent
            |> Option.map (fun fromText -> { FromText = fromText; FromRange = config.Range; ToText = toText }))

        { Range = config.Range; Message = error; SuggestedFix = Some suggestedFix; TypeChecks = config.TypeChecks }
    | Suggestion.Message(message) ->
        let errorFormatString = Resources.GetString("RulesHintSuggestion")
        let error = System.String.Format(errorFormatString, matched, message)
        { Range = config.Range; Message = error; SuggestedFix = None; TypeChecks = config.TypeChecks }

let private getMethodParameters (checkFile:FSharpCheckFileResults) (methodIdent:LongIdentWithDots) =
    let symbol =
        checkFile.GetSymbolUseAtLocation(
            methodIdent.Range.StartLine,
            methodIdent.Range.EndColumn,
            String.Empty,
            methodIdent.Lid |> List.map (fun ident -> ident.idText))

    match symbol with
    | Some(symbol) when (symbol.Symbol :? FSharpMemberOrFunctionOrValue) ->
        let symbol = symbol.Symbol :?> FSharpMemberOrFunctionOrValue

        if symbol.IsMember then symbol.CurriedParameterGroups |> Seq.tryHead
        else None
    | _ -> None

/// Check a lambda function can be replaced with a function,
/// it will not be if the lambda is automatically getting
/// converted to a delegate type e.g. Func<T>.
let private canReplaceLambdaWithFunction checkFile methodIdent index =
    let parameters = getMethodParameters checkFile methodIdent

    match parameters with
    | Some(parameters) when index < Seq.length parameters ->
        let parameter = parameters.[index]
        not (parameter.Type.HasTypeDefinition && parameter.Type.TypeDefinition.IsDelegate)
    | _ -> true

/// Check if lambda can be replaced with an identifier (cannot in the case when is a parameter with the type of a delegate).
let private (|RequiresCheck|CanBeReplaced|CannotBeReplaced|) (breadcrumbs, range) =
    match filterParens breadcrumbs with
    | AstNode.Expression(SynExpr.Tuple(_, exprs, _, _))::AstNode.Expression(SynExpr.App(ExprAtomicFlag.Atomic, _, SynExpr.DotGet(_, _, methodIdent, _), _, _))::_
    | AstNode.Expression(SynExpr.Tuple(_, exprs, _, _))::AstNode.Expression(SynExpr.App(ExprAtomicFlag.Atomic, _, SynExpr.LongIdent(_, methodIdent, _, _), _, _))::_ ->
        match exprs |> List.tryFindIndex (fun expr -> expr.Range = range) with
        | Some(index) -> RequiresCheck(index, methodIdent)
        | None -> CannotBeReplaced
    | AstNode.Expression(SynExpr.App(ExprAtomicFlag.Atomic, _, SynExpr.DotGet(_, _, methodIdent, _), _, _))::_
    | AstNode.Expression(SynExpr.App(ExprAtomicFlag.Atomic, _, SynExpr.LongIdent(_, methodIdent, _, _), _, _))::_ ->
        RequiresCheck(0, methodIdent)
    | _ -> CanBeReplaced

let private (|SuggestingReplacementOfLambda|OtherSuggestion|) = function
    | HintExpr(Expression.Lambda(_)), Suggestion.Expr(Expression.Identifier(_)) -> SuggestingReplacementOfLambda
    | _ -> OtherSuggestion

let [<Literal>] private MaxBreadcrumbs = 6
let private suggestions = ResizeArray()

let private confirmFuzzyMatch (args:AstNodeRuleParams) (hint:HintParser.Hint) =
    let breadcrumbs = args.GetParents MaxBreadcrumbs
    match (args.AstNode, hint.MatchedNode) with
    | AstNode.Expression(SynExpr.Paren(_)), HintExpr(_)
    | AstNode.Pattern(SynPat.Paren(_)), HintPat(_) -> ()
    | AstNode.Pattern(pattern), HintPat(hintPattern) when MatchPattern.matchHintPattern (pattern, hintPattern) ->
        hintError { TypeChecks = List.Empty
                    Hint = hint
                    Args = args
                    Range = pattern.Range
                    MatchedVariables = (Dictionary<_, _>())
                    ParentAstNode = None }
        |> suggestions.Add
    | AstNode.Expression(expr), HintExpr(hintExpr) ->
        let arguments =
            { MatchExpression.LambdaArguments = Map.ofList []
              MatchExpression.MatchedVariables = Dictionary<_, _>()
              MatchExpression.Expression = args.AstNode
              MatchExpression.Hint = hintExpr
              MatchExpression.FSharpCheckFileResults = args.CheckInfo
              MatchExpression.Breadcrumbs = breadcrumbs }

        match MatchExpression.matchHintExpr arguments with
        | MatchExpression.Match(typeChecks) ->
            let suggest checks =
                hintError { TypeChecks = checks
                            Hint = hint
                            Args = args
                            Range = expr.Range
                            MatchedVariables = arguments.MatchedVariables
                            ParentAstNode = (List.tryHead breadcrumbs) }
                |> suggestions.Add

            match (hint.MatchedNode, hint.Suggestion) with
            | SuggestingReplacementOfLambda ->
                match (breadcrumbs, expr.Range) with
                | RequiresCheck(index, methodIdent) ->
                    match args.CheckInfo with
                    | Some checkFile ->
                        let typeCheck = fun () -> canReplaceLambdaWithFunction checkFile methodIdent index
                        suggest (typeCheck ::typeChecks)
                    | None -> ()
                | CanBeReplaced -> suggest typeChecks
                | CannotBeReplaced -> ()
            | OtherSuggestion -> suggest typeChecks
        | MatchExpression.NoMatch -> ()
    | _ -> ()

/// Searches the abstract syntax array for possible hint matches using the hint trie.
/// Any possible matches that are found will be given to the callback function `notify`,
/// any matches found are not guaranteed and it's expected that the caller verify the match.
let private runner (config:Config) (args:AstNodeRuleParams) =
    match config.HintTrie.Lookup.TryGetValue args.NodeHashcode with
    | true, trie -> Helper.Hints.checkTrie (args.NodeIndex + 1) trie args.SyntaxArray (Dictionary<_, _>()) (confirmFuzzyMatch args)
    | false, _ -> ()

    let result = suggestions.ToArray()
    suggestions.Clear()
    result

let rule config =
    { Name = "Hints"
      Identifier = Identifiers.Hints
      RuleConfig = { AstNodeRuleConfig.Runner = runner config; Cleanup = ignore } }
    |> AstNodeRule
