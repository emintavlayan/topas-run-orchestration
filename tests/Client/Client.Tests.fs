module Client.Tests

open Fable.Mocha

open Index

let client =
    testList "Client" [
        testCase "Selecting Run tab updates selected page"
        <| fun _ ->
            let model, _ = init ()
            let model, _ = update (SelectPage Run) model

            Expect.equal model.SelectedPage Run "Selected page should be Run"
    ]

let all =
    testList "All" [
#if FABLE_COMPILER // This preprocessor directive makes editor happy
        Shared.Tests.shared
#endif
        client
        TopasSyntax.Tests.syntax
    ]

[<EntryPoint>]
let main _ = Mocha.runTests all