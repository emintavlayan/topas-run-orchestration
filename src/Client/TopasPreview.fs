module TopasPreview

open Feliz
open TopasSyntax

/// Returns the theme-aware CSS class for a TOPAS token.
let private tokenClass kind =
    match kind with
    | Plain -> ""
    | Comment -> "topas-comment"
    | ParameterType -> "topas-type"
    | ParameterPath -> "topas-path"
    | StringLiteral -> "topas-string"
    | NumberLiteral -> "topas-number"
    | Unit -> "topas-unit"
    | Operator -> "topas-operator"
    | Placeholder -> "topas-placeholder"
    | Keyword -> "topas-keyword"

/// Renders source text as escaped React content with TOPAS token colours.
let view (text: string) =
    Html.pre [
        prop.className
            "topas-preview max-h-[min(55vh,32rem)] overflow-auto rounded-box border border-base-300 bg-base-200 p-4 text-sm font-mono whitespace-pre"
        prop.tabIndex 0
        prop.ariaLabel "TOPAS input preview with syntax highlighting"
        prop.children [
            Html.code [
                prop.children [
                    for index, token in tokenize text |> List.indexed do
                        Html.span [
                            prop.key (string index)
                            prop.className (tokenClass token.Kind)
                            if token.Kind = Placeholder then
                                prop.title "Unresolved template placeholder"
                            prop.text token.Text
                        ]
                ]
            ]
        ]
    ]

