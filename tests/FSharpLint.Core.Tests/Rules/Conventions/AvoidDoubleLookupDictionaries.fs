module FSharpLint.Core.Tests.Rules.Conventions.AvoidDoubleLookupDictionaries

open NUnit.Framework

open FSharpLint.Rules

[<TestFixture>]
type TestConventionsAvoidDoubleLookupDictionaries() =
    inherit TestAstNodeRuleBase.TestAstNodeRuleBase(AvoidDoubleLookupDictionaries.rule)

    [<Test>]
    member this.AvoidDoubleLookupDictionariesShouldNotProduceError() =
        this.Parse """
let keyValuePairs =
    dict [ ("fruit", "apple"); ("vehicle", "car"); ("city", "Paris") ]

let searchKey = "fruit"

match keyValuePairs.TryGetValue searchKey with
| true, value -> printfn "The value for key %A is %A" searchKey value
| false, _ -> printfn "The key %A does not exist in the dictionary" searchKey
"""

        Assert.IsTrue this.NoErrorsExist

    [<Test>]
    member this.AvoidDoubleLookupDictionariesShouldProduceError() =
        this.Parse """
let keyValuePairs =
    dict [ ("fruit", "apple"); ("vehicle", "car"); ("city", "Paris") ]

let searchKey = "fruit"

match keyValuePairs.ContainsKey searchKey with
| true -> printfn "The value for key %A is %A" searchKey keyValuePairs.[searchKey]
| false -> printfn "The key %A does not exist in the dictionary" searchKey
"""

        Assert.IsTrue this.ErrorsExist
