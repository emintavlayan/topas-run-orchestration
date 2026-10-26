module TopasSyntax.Tests

open Fable.Mocha
open TopasSyntax

/// Reconstructs the exact source text represented by a token sequence.
let private sourceText tokens =
    tokens |> List.map _.Text |> String.concat ""

/// Finds all text fragments assigned a particular visual role.
let private tokenTexts kind tokens =
    tokens |> List.filter (fun token -> token.Kind = kind) |> List.map _.Text

let syntax =
    testList "TOPAS preview syntax" [
        testCase "Preserves whitespace, line endings and source characters"
        <| fun _ ->
            let text =
                "\t# geometry\r\ns:Ge/Head/Type = \"TsCAD\"\r\n\nd:Ge/Head/TransZ = -73.3 cm\n"

            Expect.equal (sourceText (tokenize text)) text "Highlighting must not alter the input"

        testCase "Separates parameter types, paths, signed numbers and units"
        <| fun _ ->
            let tokens = tokenize "d:Ge/Head/TransZ = -7.33e+1 cm"
            Expect.equal (tokenTexts ParameterType tokens) [ "d:" ] "Type prefix"
            Expect.equal (tokenTexts ParameterPath tokens) [ "Ge/Head/TransZ" ] "Parameter path"
            Expect.equal (tokenTexts NumberLiteral tokens) [ "-7.33e+1" ] "Signed exponent"
            Expect.equal (tokenTexts Unit tokens) [ "cm" ] "Physical unit"

        testCase "Keeps a hash inside a string separate from an inline comment"
        <| fun _ ->
            let tokens = tokenize "s:Sc/PDD/OutputFile = \"/data/run#1\" # output"
            Expect.equal (tokenTexts StringLiteral tokens) [ "\"/data/run#1\"" ] "Quoted hash"
            Expect.equal (tokenTexts Comment tokens) [ "# output" ] "Inline comment"

        testCase "Highlights unresolved placeholders inside quoted output paths"
        <| fun _ ->
            let text = "s:Sc/PDD/OutputFile = \"__OUTPUT_FILE___pdd\"\ni:Ts/Seed = __SEED__"
            let tokens = tokenize text
            Expect.equal (tokenTexts Placeholder tokens) [ "__OUTPUT_FILE__"; "__SEED__" ] "Both placeholders"
            Expect.equal (sourceText tokens) text "Output suffix must remain unchanged"

        testCase "Preserves escaped quotes, empty strings and HTML-like source"
        <| fun _ ->
            let text =
                "s:Example/Text = \"a\\\"#b\" # comment\ns:Other = \"\"\ns:Markup = \"<script>alert(1)</script>\""

            let tokens = tokenize text
            Expect.equal (sourceText tokens) text "Source must remain literal text"
            Expect.equal (tokenTexts Comment tokens) [ "# comment" ] "Escaped quote must not start a comment"

        testCase "Handles empty input and resets parameter paths on each line"
        <| fun _ ->
            Expect.equal (tokenize "") [] "Empty preview"
            let tokens = tokenize "i:Ts/Seed = 1\nb:Gr/Enable = \"False\""
            Expect.equal (tokenTexts ParameterPath tokens) [ "Ts/Seed"; "Gr/Enable" ] "Both left-hand paths"
    ]

