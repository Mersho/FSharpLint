﻿(*
    FSharpLint, a linter for F#.
    Copyright (C) 2014 Matthew Mcveigh

    This program is free software: you can redistribute it and/or modify
    it under the terms of the GNU General Public License as published by
    the Free Software Foundation, either version 3 of the License, or
    (at your option) any later version.

    This program is distributed in the hope that it will be useful,
    but WITHOUT ANY WARRANTY; without even the implied warranty of
    MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
    GNU General Public License for more details.

    You should have received a copy of the GNU General Public License
    along with this program.  If not, see <http://www.gnu.org/licenses/>.
*)

module TestCyclomaticComplexity

open NUnit.Framework
open FSharpLint.Rules.CyclomaticComplexity
open FSharpLint.Framework.Configuration
open FSharpLint.Framework.LoadAnalysers

[<Literal>]
let MaxComplexity = 1

let config = 
    Map.ofList 
        [ 
            (AnalyserName, 
                { 
                    Rules = Map.ofList []
                    Settings = Map.ofList 
                        [ 
                            ("Enabled", Enabled(true))
                            ("MaxCyclomaticComplexity", MaxCyclomaticComplexity(MaxComplexity))
                            ("IncludeMatchStatements", IncludeMatchStatements(true))
                        ]
                })
            ]

[<TestFixture>]
type TestFunctionReimplementationRules() =
    inherit TestRuleBase.TestRuleBase(Ast(findBindingVisitor), config)

    member this.AssertComplexityOf(cyclomaticComplexity, startLine, startColumn) =
        let errorFormatString = FSharpLint.Framework.Resources.GetString("RulesCyclomaticComplexityError")
        let error = System.String.Format(errorFormatString, cyclomaticComplexity, MaxComplexity)
        Assert.IsTrue(this.ErrorWithMessageExistsAt(error, startLine, startColumn))

    [<Test>]
    member this.CyclomaticComplexityOfFunction() = 
        this.Parse """
module Program

let x () =
    if true then
        if true then
            ()
        else
            ()
    else
        for i in [] do
            ()
"""

        this.AssertComplexityOf(3, 4, 4)