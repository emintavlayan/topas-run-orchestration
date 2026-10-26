module TopasSyntax

open System
open System.Text.RegularExpressions

/// Represents the visual role of a TOPAS text token.
type TokenKind =
    | Plain
    | Comment
    | ParameterType
    | ParameterPath
    | StringLiteral
    | NumberLiteral
    | Unit
    | Operator
    | Placeholder
    | Keyword

/// Represents an unchanged source-text fragment and its visual role.
type Token = { Kind: TokenKind; Text: string }

let private lexemes =
    Regex(
        """"(?:\\.|[^"\\\r\n])*"?|#[^\r\n]*|__[A-Z](?:[A-Z0-9_]*[A-Z0-9])?__|[A-Za-z][A-Za-z0-9]*:|[-+]?(?:\d+(?:\.\d*)?|\.\d+)(?:[eE][-+]?\d+)?|[A-Za-z_][A-Za-z0-9_./-]*|\s+|."""
    )

let private placeholders = Regex("__[A-Z](?:[A-Z0-9_]*[A-Z0-9])?__")
let private numbers = Regex("""^[-+]?(?:\d+(?:\.\d*)?|\.\d+)(?:[eE][-+]?\d+)?$""")

let private parameterTypes =
    set [
        "d:"
        "u:"
        "i:"
        "b:"
        "s:"
        "dv:"
        "uv:"
        "iv:"
        "bv:"
        "sv:"
        "dc:"
        "uc:"
        "ic:"
        "bc:"
        "sc:"
    ]

let private units =
    set [
        "nm"
        "um"
        "mm"
        "cm"
        "m"
        "mm2"
        "cm2"
        "m2"
        "mm3"
        "cm3"
        "m3"
        "deg"
        "rad"
        "eV"
        "keV"
        "MeV"
        "GeV"
        "TeV"
        "Gy"
        "cGy"
        "ps"
        "ns"
        "us"
        "ms"
        "s"
        "min"
        "h"
        "g"
        "kg"
        "g/cm3"
        "kg/m3"
        "K"
        "tesla"
        "T"
        "volt"
        "V"
        "pascal"
        "Pa"
        "bar"
        "atmosphere"
    ]

/// Classifies a lexeme without interpreting or changing its parameter value.
let private classifyToken (beforeAssignment: bool) (text: string) =
    if String.IsNullOrWhiteSpace text then
        Plain
    elif text.StartsWith "#" then
        Comment
    elif text.StartsWith "\"" then
        StringLiteral
    elif placeholders.IsMatch text then
        Placeholder
    elif parameterTypes.Contains(text.ToLowerInvariant()) then
        ParameterType
    elif text = "=" || text = "+" || text = "-" || text = "*" then
        Operator
    elif text = "includeFile" || text = "inheritedValue" then
        Keyword
    elif numbers.IsMatch text then
        NumberLiteral
    elif beforeAssignment then
        ParameterPath
    elif units.Contains text then
        Unit
    elif text.Contains "/" then
        ParameterPath
    else
        Plain

/// Separates unresolved placeholders inside a quoted string while preserving every character.
let private tokenizeString (text: string) =
    let matches = placeholders.Matches text |> Seq.cast<Match> |> Seq.toList

    /// Collects string segments and placeholder tokens in source order.
    let rec collect offset (remaining: Match list) =
        match remaining with
        | [] ->
            if offset < text.Length then
                [
                    {
                        Kind = StringLiteral
                        Text = text.Substring offset
                    }
                ]
            else
                []
        | matched :: tail ->
            let prefix =
                if matched.Index > offset then
                    [
                        {
                            Kind = StringLiteral
                            Text = text.Substring(offset, matched.Index - offset)
                        }
                    ]
                else
                    []

            prefix
            @ [
                {
                    Kind = Placeholder
                    Text = matched.Value
                }
            ]
            @ collect (matched.Index + matched.Length) tail

    collect 0 matches

/// Tokenizes TOPAS source for display without altering source text or validating physics.
let tokenize (text: string) : Token list =
    /// Adds one lexeme and tracks whether the current line is before its assignment.
    let collect (beforeAssignment, reversedTokens) (matched: Match) =
        let value = matched.Value
        let kind = classifyToken beforeAssignment value

        let tokens =
            if kind = StringLiteral then
                tokenizeString value
            else
                [ { Kind = kind; Text = value } ]

        let nextBeforeAssignment =
            if value.Contains "\n" || value.Contains "\r" then true
            elif kind = Operator && value = "=" then false
            else beforeAssignment

        nextBeforeAssignment, List.rev tokens @ reversedTokens

    lexemes.Matches text
    |> Seq.cast<Match>
    |> Seq.fold collect (true, [])
    |> snd
    |> List.rev

