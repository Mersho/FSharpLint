module FSharpLint.Core.Tests.Rules.Conventions.UnreachableAsyncException

open NUnit.Framework
open FSharpLint.Rules

[<TestFixture>]
type TestConventionsUnreachableAsyncExceptionKeyword() =
    inherit TestAstNodeRuleBase.TestAstNodeRuleBase(UnreachableAsyncException.rule)

    [<Test>]
    member this.UnreachableAsyncExceptionShouldNotProduceError() =
        this.Parse """
let Foo(): Async<Result<'Bar, string>> =
    async {
        try
            let! result = DoSomething()
            return Ok(result)
        with
        | ex -> return Error("An error occurred: " + ex.Message)
    }
"""

        Assert.IsTrue this.NoErrorsExist

    [<Test>]
    member this.UnreachableAsyncExceptionShouldProduceError() =
        this.Parse """
let Foo() =
    try
        1
    with
    | ex -> failwith "exception raised on DoSomething()"
"""

        Assert.IsTrue this.ErrorsExist
