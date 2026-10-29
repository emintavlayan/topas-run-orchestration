module CollectViews

open Feliz
open GenerateTypes
open CollectLogic
open WizardShell
open SAFE

/// Combines display path parts using a forward slash for browser-rendered paths.
let private joinDisplayPath (parts: string list) =
    parts
    |> List.map (fun part -> part.Trim().Trim('/', '\\'))
    |> List.filter (System.String.IsNullOrWhiteSpace >> not)
    |> String.concat "/"

/// Returns classes for collect preflight status badges.
let collectStatusClass (ok: bool) =
    if ok then
        "badge badge-success badge-sm"
    else
        "badge badge-error badge-sm"

/// Renders collect welcome content.
let viewCollectWelcome () =
    Html.div [
        prop.className "space-y-2 text-sm text-base-content/80"
        prop.children [
            Html.p "Collect reads TOPAS CSV/log outputs for one batch."
            Html.p "It checks expected files, merges node outputs per phase-space, merges phase-space dose, and computes Type A uncertainty of the summed dose."
            Html.p "The final outputs are merged-over-nodes, merged-over-phsp/dose_merged.csv, and dose_with_uncertainty.csv."
        ]
    ]

/// Renders collect batch table.
let viewCollectBatchSelection (collect: CollectModel) (dispatch: Msg -> unit) =
    match collect.Batches with
    | NotStarted -> Html.p "Collect batches not loaded."
    | Loading _ -> Html.p "Loading collect batches..."
    | Loaded batches when batches.IsEmpty -> Html.p "No collect batches available."
    | Loaded batches ->
        Html.div [
            prop.className "min-h-0"
            prop.children [
                viewTableScroll (
                    Html.table [
                        prop.className "table table-zebra text-sm"
                        prop.children [
                            Html.thead [
                                Html.tr [
                                    Html.th [ prop.className "border-b border-base-300 px-3 py-2 text-left font-semibold"; prop.text "Select" ]
                                    Html.th [ prop.className "border-b border-base-300 px-3 py-2 text-left font-semibold"; prop.text "Seed base" ]
                                    Html.th [ prop.className "border-b border-base-300 px-3 py-2 text-left font-semibold"; prop.text "Created" ]
                                    Html.th [ prop.className "border-b border-base-300 px-3 py-2 text-left font-semibold"; prop.text "Runs" ]
                                    Html.th [ prop.className "border-b border-base-300 px-3 py-2 text-left font-semibold"; prop.text "Nodes" ]
                                    Html.th [ prop.className "border-b border-base-300 px-3 py-2 text-left font-semibold"; prop.text "Phase-spaces" ]
                                    Html.th [ prop.className "border-b border-base-300 px-3 py-2 text-left font-semibold"; prop.text "Run status" ]
                                    Html.th [ prop.className "border-b border-base-300 px-3 py-2 text-left font-semibold"; prop.text "Collect status" ]
                                    Html.th [ prop.className "border-b border-base-300 px-3 py-2 text-left font-semibold"; prop.text "Latest collection folder" ]
                                ]
                            ]
                            Html.tbody [
                                for batch in batches do
                                    let selectable = isCollectBatchSelectable batch
                                    let selected = collect.SelectedSeedBase = Some batch.SeedBase

                                    Html.tr [
                                        prop.className (if selected then "bg-base-200" else "")
                                        prop.children [
                                            Html.td [
                                                prop.className "border-b border-base-200 px-3 py-2"
                                                prop.children [
                                                    Html.input [
                                                        prop.type'.radio
                                                        prop.className "radio radio-primary radio-sm"
                                                        prop.name "collect-batch"
                                                        prop.isChecked selected
                                                        prop.disabled (not selectable)
                                                        prop.onChange (fun (_: bool) -> dispatch (SelectCollectBatch batch.SeedBase))
                                                    ]
                                                ]
                                            ]
                                            Html.td [ prop.className "border-b border-base-200 px-3 py-2"; prop.text batch.SeedBase ]
                                            Html.td [ prop.className "border-b border-base-200 px-3 py-2"; prop.text batch.CreatedAt ]
                                            Html.td [ prop.className "border-b border-base-200 px-3 py-2"; prop.text $"{batch.GeneratedRunCount}" ]
                                            Html.td [ prop.className "border-b border-base-200 px-3 py-2"; prop.text $"{batch.NodeCount}" ]
                                            Html.td [ prop.className "border-b border-base-200 px-3 py-2"; prop.text $"{batch.PhaseSpaceCount}" ]
                                            Html.td [ prop.className "border-b border-base-200 px-3 py-2"; prop.text (defaultArg batch.RunStatus "-") ]
                                            Html.td [ prop.className "border-b border-base-200 px-3 py-2"; prop.text batch.CollectStatus ]
                                            Html.td [ prop.className "border-b border-base-200 px-3 py-2 break-all"; prop.text (defaultArg batch.LatestCollectionFolder "-") ]
                                        ]
                                    ]
                            ]
                        ]
                    ]
                )
            ]
        ]

/// Renders collect preflight review.
let viewCollectPreflight (collect: CollectModel) (dispatch: Msg -> unit) =
    match collect.Preview with
    | NotStarted -> Html.p "No preflight data loaded."
    | Loading _ -> Html.p "Loading collect preflight..."
    | Loaded preview ->
        Html.div [
            prop.className "space-y-4"
            prop.children [
                Html.div [
                    prop.className "grid gap-2 text-sm md:grid-cols-2"
                    prop.children [
                        Html.p [ prop.text $"Expected runs: {preview.Preflight.ExpectedRunCount}" ]
                        Html.p [ prop.text $"Effective runs: {preview.Preflight.EffectiveRunCount}" ]
                        Html.p [ prop.text $"Effective phase-spaces: {preview.Preflight.EffectivePhaseSpaceCount}" ]
                        Html.p [ prop.text $"Effective nodes: {preview.Preflight.EffectiveNodeCount}" ]
                        Html.p [ prop.text ("Scorers: " + String.concat ", " preview.Preflight.Scorers) ]
                        Html.p [ prop.text $"CSV found/missing: {preview.Preflight.FoundCsvCount}/{preview.Preflight.MissingCsvCount}" ]
                        Html.p [ prop.text $"Logs found/missing: {preview.Preflight.FoundLogCount}/{preview.Preflight.MissingLogCount}" ]
                    ]
                ]
                Html.div [
                    prop.className "space-y-1 text-sm"
                    prop.children [
                        Html.p [
                            prop.text (
                                if preview.Preflight.ExcludedPhaseSpaceIndexes.IsEmpty then
                                    "Excluded phase-space indexes: none"
                                else
                                    "Excluded phase-space indexes: "
                                    + System.String.Join(", ", preview.Preflight.ExcludedPhaseSpaceIndexes)
                            )
                        ]
                        Html.p [
                            prop.text (
                                if preview.Preflight.ExcludedNodeDigits.IsEmpty then
                                    "Excluded nodes: none"
                                else
                                    "Excluded nodes: " + System.String.Join(", ", preview.Preflight.ExcludedNodeDigits)
                            )
                        ]
                    ]
                ]
                if preview.Preflight.FileIssues.IsEmpty then
                    Html.none
                else
                    Html.div [
                        prop.className "space-y-3"
                        prop.children [
                            Html.div [
                                prop.className "flex flex-wrap gap-2"
                                prop.children [
                                    Html.button [
                                        prop.className "btn btn-outline btn-sm"
                                        prop.text "Skip failed phase-space files"
                                        prop.onClick (fun _ ->
                                            let excluded =
                                                preview.Preflight.FileIssues
                                                |> List.map _.PhaseSpaceIndex
                                                |> List.distinct
                                                |> List.sort

                                            dispatch (ExcludeCollectPhaseSpaces excluded))
                                    ]
                                    Html.button [
                                        prop.className "btn btn-outline btn-sm"
                                        prop.text "Skip failed nodes"
                                        prop.onClick (fun _ ->
                                            let excluded =
                                                preview.Preflight.FileIssues
                                                |> List.map _.NodeDigit
                                                |> List.distinct
                                                |> List.sort

                                            dispatch (ExcludeCollectNodes excluded))
                                    ]
                                ]
                            ]
                            Html.div [
                                prop.className "max-h-[45vh]"
                                prop.children [
                                    viewTableScroll (
                                        Html.table [
                                            prop.className "table table-zebra table-sm"
                                            prop.children [
                                                Html.thead [
                                                    Html.tr [
                                                        Html.th "Run id"
                                                        Html.th "Phase-space"
                                                        Html.th "Node"
                                                        Html.th "File kind"
                                                        Html.th "Problem"
                                                        Html.th "Message"
                                                    ]
                                                ]
                                                Html.tbody [
                                                    for issue in preview.Preflight.FileIssues do
                                                        Html.tr [
                                                            Html.td issue.RunId
                                                            Html.td issue.PhaseSpaceIndex
                                                            Html.td issue.NodeDigit
                                                            Html.td issue.FileKind
                                                            Html.td issue.Problem
                                                            Html.td (defaultArg issue.Message "-")
                                                        ]
                                                ]
                                            ]
                                        ]
                                    )
                                ]
                            ]
                        ]
                    ]
                viewTableScroll (
                    Html.table [
                        prop.className "table table-zebra text-sm"
                        prop.children [
                            Html.thead [
                                Html.tr [
                                    Html.th [ prop.className "border-b border-base-300 px-3 py-2 text-left font-semibold"; prop.text "Check" ]
                                    Html.th [ prop.className "border-b border-base-300 px-3 py-2 text-left font-semibold"; prop.text "Status" ]
                                    Html.th [ prop.className "border-b border-base-300 px-3 py-2 text-left font-semibold"; prop.text "Details" ]
                                ]
                            ]
                            Html.tbody [
                                for check in preview.Preflight.Checks do
                                    Html.tr [
                                        Html.td [ prop.className "border-b border-base-200 px-3 py-2"; prop.text check.Name ]
                                        Html.td [
                                            prop.className "border-b border-base-200 px-3 py-2"
                                            prop.children [ Html.span [ prop.className (collectStatusClass check.Ok); prop.text (if check.Ok then "OK" else "Failed") ] ]
                                        ]
                                        Html.td [ prop.className "border-b border-base-200 px-3 py-2"; prop.text (defaultArg check.Message "-") ]
                                    ]
                            ]
                        ]
                    ]
                )
                if preview.Preflight.MissingFiles.IsEmpty then
                    Html.none
                else
                    Html.div [
                        Html.h4 [ prop.className "font-semibold"; prop.text "Missing files" ]
                        Html.div [
                            prop.className "mt-2 max-h-[30vh]"
                            prop.children [
                                viewScrollPanel [
                                    Html.ul [
                                        prop.className "list-disc pl-5 text-sm"
                                        prop.children [
                                            for missing in preview.Preflight.MissingFiles do
                                                Html.li [ prop.className "break-all"; prop.text $"{missing.FileKind}: {missing.Path}" ]
                                        ]
                                    ]
                                ]
                            ]
                        ]
                    ]
            ]
        ]

/// Renders collect merge review.
let viewCollectMergeReview (collect: CollectModel) =
    match collect.Preview with
    | Loaded preview ->
        let collectionRootFolder = joinDisplayPath [ "outputs"; preview.SeedBase ]
        let latestCollectionFolder = defaultArg preview.LatestCollectionFolder "-"

        Html.div [
            prop.className "space-y-3 text-sm text-base-content/80"
            prop.children [
                match collect.CollectResult with
                | Loading _ ->
                    Html.div [
                        prop.className "alert alert-info"
                        prop.children [
                            Html.span [ prop.className "loading loading-spinner loading-sm" ]
                            Html.span "Collect is running. This may take several minutes for large TOPAS CSV files."
                        ]
                    ]
                | _ -> Html.none
                if preview.HasCollectedBefore then
                    Html.div [
                        prop.className "alert alert-info"
                        prop.children [
                            Html.div [
                                prop.className "space-y-1"
                                prop.children [
                                    Html.p "This run has already been collected."
                                    Html.p "Collecting again will not overwrite previous outputs."
                                    Html.p $"A new timestamped output folder will be created under {collectionRootFolder}/."
                                    Html.p $"Latest collection: {latestCollectionFolder}"
                                ]
                            ]
                        ]
                    ]
                else
                    Html.none
                Html.p $"Output folder: {preview.OutputFolder}"
                Html.p $"Manifest: {preview.ManifestPath}"
                for scorer in preview.Scorers do
                    Html.div [
                        prop.className "space-y-1 break-all"
                        prop.children [
                            Html.h4 [ prop.className "font-semibold"; prop.text $"Scorer: {scorer.Scorer}" ]
                            Html.p $"Final merged dose: {scorer.SummaryPath}"
                            Html.p $"Dose uncertainty: {scorer.UncertaintyPath}"
                            Html.p $"Scorer manifest: {scorer.ManifestPath}"
                        ]
                    ]
                Html.p $"Effective run count: {preview.Preflight.EffectiveRunCount}"
                Html.p $"Effective phase-space count: {preview.Preflight.EffectivePhaseSpaceCount}"
                Html.p $"Effective node count: {preview.Preflight.EffectiveNodeCount}"
                Html.h4 [ prop.className "font-semibold"; prop.text "Planned merged files" ]
                Html.div [
                    prop.className "max-h-[40vh]"
                    prop.children [
                        viewScrollPanel [
                            Html.ul [
                                prop.className "list-disc pl-5"
                                prop.children [
                                    for path in preview.PlannedMergedFiles do
                                        Html.li [ prop.className "break-all"; prop.text path ]
                                ]
                            ]
                        ]
                    ]
                ]
                Html.p "Node merge: sums dose across nodes for each phase-space file."
                Html.p "Final merge: sums merged-over-nodes phase-space files into one final summed dose_merged.csv."
                Html.p "Uncertainty: computes the Type A one-sigma uncertainty of that summed dose directly from the independent raw node/phase-space CSV batches, not a standard error of the mean."
            ]
        ]
    | Loading _ -> Html.p "Loading collect review..."
    | _ -> Html.p "No collect preview loaded."

/// Renders collect result content.
let viewCollectResult (collect: CollectModel) =
    match collect.CollectResult with
    | Loading _ -> Html.p "Running collect..."
    | Loaded value ->
        Html.div [
            prop.className "space-y-2 text-sm text-base-content/80"
            prop.children [
                Html.p [ prop.className "font-semibold text-emerald-700"; prop.text "Collect completed." ]
                Html.p $"Status: {value.Status}"
                Html.p $"CSV files read: {value.CsvReadCount}"
                Html.p $"Logs found: {value.LogFoundCount}"
                Html.p $"Merged phase-space count: {value.MergedPhaseSpaceCount}"
                Html.p [ prop.className "break-all"; prop.text $"Output folder: {value.OutputFolder}" ]
                for scorer in value.Scorers do
                    Html.div [
                        prop.className "space-y-1 break-all"
                        prop.children [
                            Html.h4 [ prop.className "font-semibold"; prop.text $"Scorer: {scorer.Scorer}" ]
                            Html.p $"Final merged dose: {scorer.SummaryPath}"
                            Html.p $"Dose uncertainty: {scorer.UncertaintyPath}"
                            Html.p $"Scorer manifest: {scorer.ManifestPath}"
                        ]
                    ]
                Html.p [ prop.className "break-all"; prop.text $"Manifest path: {value.ManifestPath}" ]
                Html.h4 [ prop.className "font-semibold"; prop.text "Merged files" ]
                Html.div [
                    prop.className "max-h-[40vh]"
                    prop.children [
                        viewScrollPanel [
                            Html.ul [
                                prop.className "list-disc pl-5"
                                prop.children [
                                    for merged in value.MergedFiles do
                                        Html.li [
                                            prop.className "break-all"
                                            prop.text $"{merged.PhaseSpaceIndex}: {merged.MergedFilePath} (source csv count: {merged.SourceCsvCount})"
                                        ]
                                ]
                            ]
                        ]
                    ]
                ]
            ]
        ]
    | _ ->
        match collect.Error with
        | Some message ->
            Html.div [
                prop.className "alert alert-error text-sm"
                prop.text message
            ]
        | None -> Html.p "No collect result yet."

/// Renders collect step content.
let viewCollectStep (collect: CollectModel) (dispatch: Msg -> unit) =
    match collect.Step with
    | CollectWelcome -> viewCollectWelcome ()
    | CollectSelectBatch -> viewCollectBatchSelection collect dispatch
    | CollectPreflightReview -> viewCollectPreflight collect dispatch
    | CollectMergeReview -> viewCollectMergeReview collect
    | CollectResult -> viewCollectResult collect

/// Renders full collect wizard page.
let viewCollectPage (collect: CollectModel) (dispatch: Msg -> unit) =
    let steps =
        [
            { Title = "Welcome"; Description = "Review what Collect reads and writes." }
            { Title = "Batch"; Description = "Select a batch with TOPAS outputs." }
            { Title = "Preflight"; Description = "Check expected CSV and log files." }
            { Title = "Review"; Description = "Review planned merge and uncertainty outputs." }
            { Title = "Result"; Description = "Review collected outputs." }
        ]
    let currentStepIndex =
        collect.Step
        |> function
            | CollectWelcome -> 0
            | CollectSelectBatch -> 1
            | CollectPreflightReview -> 2
            | CollectMergeReview -> 3
            | CollectResult -> 4
    viewWizardShell
        steps
        currentStepIndex
        (viewCollectStep collect dispatch)
        collect.Error
        (showPreviousCollectButton collect.Step)
        (collectPrimaryButtonText collect)
        (disableCollectPrimaryButton collect)
        (fun () -> dispatch CancelCollectWizard)
        (fun () -> dispatch PreviousCollectStep)
        (fun () -> dispatch NextCollectStep)

