module FSharpLint.Core.Tests.Rules.Conventions.UnneededMutableKeyword

open NUnit.Framework
open FSharpLint.Framework.Rules
open FSharpLint.Rules

[<TestFixture>]
type TestConventionsUnneededMutableKeyword() =
    inherit TestAstNodeRuleBase.TestAstNodeRuleBase(UnneededMutableKeyword.rule)

    [<Test>]
    member this.UnneededMutableKeywordShouldNotProduceError() =
        this.Parse """
let mutable foo = 1
foo <- foo + 1"""

        Assert.IsTrue this.NoErrorsExist

    [<Test>]
    member this.UnneededMutableKeywordShouldProduceError() =
        this.Parse """
let mutable foo = 1"""

        Assert.IsTrue this.ErrorsExist

    [<Test>]
    member this.UnneededMutableKeywordShouldProduceError_1() =
        this.Parse """
let mutable foo = 1
let barFunc () =
    let foo = 2
    ()"""

        Assert.IsTrue this.ErrorsExist
