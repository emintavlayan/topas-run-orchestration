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
            topNavButton model.SelectedPage RunControl dispatch
            topNavButton model.SelectedPage Collect dispatch
            topNavButton model.SelectedPage Import dispatch
            topNavButton model.SelectedPage Analyse dispatch
        ]
    ]

/// Renders the top-right daisyUI theme selector.
let viewThemeSelector (selectedTheme: ThemeName) (dispatch: Msg -> unit) =
    Html.div [
        prop.className "form-control"
        prop.children [
            Html.select [
                prop.className "select select-bordered select-sm w-28"
                prop.custom ("aria-label", "Theme")
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
        prop.className "flex flex-wrap items-center gap-3 rounded-box border border-base-300 bg-base-100 px-4 py-3"
        prop.children [
            Html.div [
                prop.className "shrink-0 pr-2"
                prop.children [
                    Html.div [
                        prop.children [
                            Html.h1 [
                                prop.className "text-sm font-semibold tracking-tight whitespace-nowrap"
                                prop.text "TOPAS Run Orchestration"
                            ]
                        ]
                    ]
                ]
            ]
            Html.div [
                prop.className "order-3 w-full overflow-x-auto lg:order-none lg:w-auto lg:flex-1"
                prop.children [ viewWorkflowNavigation model dispatch ]
            ]
            Html.div [
                prop.className "ml-auto"
                prop.children [
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
    | Import -> ImportViews.View ()
    | Analyse -> AnalysisViews.View ()
    | RunControl -> RunControlViews.View ()

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

