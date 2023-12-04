module FSharpLint.Core.Tests.Rules.Conventions.SuggestUseAutoProperty

open NUnit.Framework
open FSharpLint.Framework.Rules
open FSharpLint.Rules

[<TestFixture>]
type TestSuggestUseAutoProperty() =
    inherit TestAstNodeRuleBase.TestAstNodeRuleBase(SuggestUseAutoProperty.rule)

    [<Test>]
    member this.``Should suggest usage of auto-property for property that only returns immutable value`` () =
        this.Parse """
type Foo(content: int) =
    member x.Content = content
"""

        Assert.IsTrue(this.ErrorsExist)

    [<Test>]
    member this.``Should suggest usage of auto-property for property that only returns literal`` () =
        this.Parse """
type Foo() =
    member x.Content = 42
"""

        Assert.IsTrue(this.ErrorsExist)
