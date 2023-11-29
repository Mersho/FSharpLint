﻿module FSharpLint.Core.Tests.Rules.Conventions.AsyncExceptionWithoutReturn

open NUnit.Framework
open FSharpLint.Framework.Rules
open FSharpLint.Rules

[<TestFixture>]
type TestAsyncExceptionWithoutReturn() =
    inherit TestAstNodeRuleBase.TestAstNodeRuleBase(AsyncExceptionWithoutReturn.rule)

    [<Test>]
    member this.AsyncExceptionWithoutReturn() = 
        this.Parse("""
namespace Program

let someAsyncFunction = async {
    raise (new System.Exception("An error occurred."))
    return true
    }""")

        Assert.IsTrue this.ErrorsExist

    [<Test>]
    member this.AsyncExceptionWithoutReturn_2() = 
        this.Parse("""
namespace Program

let someAsyncFunction = async {
    return raise (new System.Exception("An error occurred."))
    }""")

        this.AssertNoWarnings()

    [<Test>]
    member this.AsyncExceptionWithoutReturnOnFailWith() =          
        this.Parse("""
namespace Program

let someAsyncFunction = async {
    failwith "An error occurred."
    return true
    }""")

        Assert.IsTrue this.ErrorsExist

    [<Test>]
    member this.AsyncExceptionWithoutReturnOnFailWith_2() =          
        this.Parse("""
namespace Program

let someAsyncFunction = async {
    failwithf "An error occurred."
    return true
    }""")

        Assert.IsTrue this.ErrorsExist

    [<Test>]
    member this.AsyncExceptionWithoutReturnOnFailWithf() = 
        this.Parse("""
namespace Program

let someAsyncFunction = async {
    return failwith "An error occurred."
    }""")

        this.AssertNoWarnings()

    [<Test>]
    member this.AsyncExceptionWithoutReturnOnFailWithf_2() = 
        this.Parse("""
namespace Program

let someAsyncFunction = async {
    return failwithf "An error occurred."
    }""")

        this.AssertNoWarnings()

    [<Test>]
    member this.AsyncExceptionWithoutReturnInnerExpression() = 
        this.Parse("""
namespace Program

let someAsyncFunction =
    async {
        if 2 = 2 then
            raise (new System.Exception("An error occurred."))

        return true
    }""")

        Assert.IsTrue this.ErrorsExist
