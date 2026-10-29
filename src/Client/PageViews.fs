module PageViews

open Feliz
open GenerateLogic
open GenerateTypes
open GenerateViews
open RunViews
open CollectViews
open Shared
open SAFE

/// Returns the daisyUI theme token for the selected shell theme.
let themeValue (theme: ThemeName) =
    match theme with
    | Light -> "light"
    | Dark -> "dark"
    | Corporate -> "corporate"
    | Night -> "night"
    | Cyberpunk -> "cyberpunk"

/// Returns the display label for the selected shell theme.
let themeLabel (theme: ThemeName) =
    match theme with
    | Light -> "Light"
    | Dark -> "Dark"
    | Corporate -> "Corporate"
    | Night -> "Night"
    | Cyberpunk -> "Cyberpunk"

/// Parses a daisyUI theme token into a shell theme.
let themeFromValue (value: string) =
    match value with
    | "dark" -> Dark
    | "corporate" -> Corporate
    | "night" -> Night
    | "cyberpunk" -> Cyberpunk
    | _ -> Light

/// Returns classes for top-level workflow navigation buttons.
let topNavButtonClass (isSelected: bool) =
    if isSelected then
        "join-item btn btn-primary btn-sm normal-case whitespace-nowrap"
    else
        "join-item btn btn-outline btn-sm normal-case whitespace-nowrap"

/// Renders one top-level navigation button.
let topNavButton (selectedPage: Page) (page: Page) (dispatch: Msg -> unit) =
    let isSelected = selectedPage = page

    Html.button [
        prop.className (topNavButtonClass isSelected)
        prop.text (pageLabel page)
        prop.onClick (fun _ -> dispatch (SelectPage page))
    ]

/// Renders the workflow navigation group.
let viewWorkflowNavigation (model: Model) (dispatch: Msg -> unit) =
    Html.div [
        prop.className "join"
        prop.children [
            topNavButton model.SelectedPage Generate dispatch
            topNavButton model.SelectedPage Run dispatch
            topNavButton model.SelectedPage Collect dispatch
            topNavButton model.SelectedPage Analyse dispatch
        ]
    ]

/// Renders the top-right daisyUI theme selector.
let viewThemeSelector (selectedTheme: ThemeName) (dispatch: Msg -> unit) =
    Html.div [
        prop.className "form-control"
        prop.children [
            Html.select [
                prop.className "select select-bordered select-sm min-w-36"
                prop.value (themeValue selectedTheme)
                prop.onChange (fun (value: string) -> value |> themeFromValue |> SelectTheme |> dispatch)
                prop.children [
                    for theme in [ Light; Dark; Corporate; Night; Cyberpunk ] do
                        Html.option [
                            prop.value (themeValue theme)
                            prop.text (themeLabel theme)
                        ]
                ]
            ]
        ]
    ]

/// Renders the top application navbar.
let viewTopNavbar (model: Model) (dispatch: Msg -> unit) =
    Html.div [
        prop.className "navbar rounded-box border border-base-300 bg-base-100 px-4 shadow-md"
        prop.children [
            Html.div [
                prop.className "navbar-start w-auto min-w-fit pr-4"
                prop.children [
                    Html.div [
                        prop.children [
                            Html.h1 [
                                prop.className "text-lg font-bold tracking-tight md:text-xl"
                                prop.text "Topas Run Orchestration"
                            ]
                        ]
                    ]
                ]
            ]
            Html.div [
                prop.className "navbar-center hidden flex-1 justify-start lg:flex"
                prop.children [ viewWorkflowNavigation model dispatch ]
            ]
            Html.div [
                prop.className "navbar-end flex-1 gap-3"
                prop.children [
                    Html.div [
                        prop.className "flex lg:hidden"
                        prop.children [ viewWorkflowNavigation model dispatch ]
                    ]
                    viewThemeSelector model.SelectedTheme dispatch
                ]
            ]
        ]
    ]

/// Renders page content based on selected top-level tab.
let viewPageContent (model: Model) (dispatch: Msg -> unit) =
    match model.SelectedPage with
    | Generate -> viewGeneratePage model.Generate dispatch
    | Run ->
        let appRoot =
            match model.Generate.Config with
            | Loaded config -> Some config.AppRoot
            | _ -> None

        viewRunPage appRoot model.Run dispatch
    | Collect -> viewCollectPage model.Collect dispatch
    | Analyse -> AnalysisViews.view ()

/// Renders the client landing page and selected content.
let view (model: Model) (dispatch: Msg -> unit) =
    Html.main [
        prop.className "min-h-screen w-full bg-base-200 text-base-content"
        prop.custom ("data-theme", themeValue model.SelectedTheme)
        prop.children [
            Html.section [
                prop.className "mx-auto w-[96vw] max-w-[1600px] p-4"
                prop.children [
                    viewTopNavbar model dispatch
                    Html.div [
                        prop.className "mt-4"
                        prop.children [ viewPageContent model dispatch ]
                    ]
                ]
            ]
        ]
    ]

