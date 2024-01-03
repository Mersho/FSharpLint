module FSharpLint.Core.Tests.Rules.Conventions.FavourAsKeyword

open NUnit.Framework
open FSharpLint.Framework.Rules
open FSharpLint.Rules

[<TestFixture>]
type TestConventionsFavourAsKeyword() =
    inherit TestAstNodeRuleBase.TestAstNodeRuleBase(FavourAsKeyword.rule)

    [<Test>]
    member this.FavourAsKeywordShouldNotProduceError() =
        this.Parse """
let foo = "baz"
match foo with
| "baz" as bar -> printfn "bar is %s" bar
| _ -> printfn "bar is not baz" """

        Assert.IsTrue this.NoErrorsExist

    [<Test>]
    member this.FavourAsKeywordShouldProduceError_1() =
        this.Parse """
let foo = "baz"
match foo with
| bar when bar = "baz" -> printfn "bar is baz"
| _ -> printfn "bar is not baz" """

        Assert.IsTrue this.ErrorsExist

    [<Test>]
    member this.FavourAsKeywordShouldProduceError_2() =
        this.Parse """
let foo = 12
match foo with
| bar when bar = 12 -> printfn "bar is 12"
| _ -> printfn "bar is not baz" """

        Assert.IsTrue this.ErrorsExist

    [<Test>]
    member this.FavourAsKeywordShouldProduceError_3() =
        this.Parse """
let foo = "baz"
match foo with
| bar when bar = "baz" -> printfn "bar is baz"
| buzz when buzz = "buzz" -> printfn "bar is buzz"
| _ -> printfn "bar is not baz" """

        Assert.IsTrue(this.ErrorExistsAt(4, 2))
        Assert.IsTrue(this.ErrorExistsAt(5, 2))
