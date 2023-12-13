namespace FSharpLint.Core

[<AutoOpen>]
module Prelude =

    module Async =
        let combine operation firstAsync secondAsync = async {
            let! x = firstAsync 
            let! y = secondAsync 
            return operation x y }

        let map operation xAsync = async {
            let! x = xAsync
            return operation x }