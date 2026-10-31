# Analyse collected outputs

[README](../README.md) · [All documentation](index.md)

Analyse reads final `dose_with_uncertainty.csv` files from the configured output root. The CSV's dose and standard uncertainty are both in Gy. Coordinate columns retain TOPAS bin coordinates: Collect does not convert them to millimetres.

## Geometry detection

For `outputs/1113/{timestamp}/pdd/dose_with_uncertainty.csv`, the scorer manifest identifies retained run IDs. Analyse reads their generated `inputs/1113/{runId}.txt` files, matches the scorer OutputFile suffix, and resolves its Component, bin count, half-length, translations and parent chain. All contributing inputs must agree. Current unrotated TsBox X/Y profiles and water-parent TsCylinder/TsBox PDD scorers are supported. Literal mm/cm/m/um values and direct parameter references are resolved; expressions and rotated ancestry fall back to explicit mapping.

For the current water-tank templates:

- PDD: 25 bins over 50 mm; depth centres 1, 3, …, 49 mm from the water entrance face.
- X/Y profiles: 300 bins over 600 mm; centres −299, −297, …, +299 mm.

Input files are the archived generated inputs, not current templates. Missing inputs, inconsistent geometry, mismatched bin counts and ambiguous scorers do not enable inferred distance metrics. The interface reports **Geometry unavailable**, shows the reason under Settings and offers a manual mapping editor. Input-derived geometry is active immediately with no user confirmation. Manually saved overrides are atomically saved beside the CSV as `.analysis.json`; the CSV is unchanged. Saved overrides take precedence. Historical manually renamed/default scorer collections require explicit mapping. Multidimensional scorers are rejected rather than averaged implicitly.

## Curve processing

The workspace has a compact run selector and **Profiles / Metrics / Settings** tabs. Each run ID expands to show its collection folders, newest first, with the latest selected by default. Selecting a run loads all scorer datasets from exactly one collection; selecting another collection replaces that run’s curves. A selected collection remains pinned while the run is loaded. Removing a run removes its curves and any reference to them. **X**, **Y** and **PDD** buttons choose the displayed role without changing the selected runs. Metrics use one run per row, with full dataset paths available on hover and in exports. Settings contains normalization, equations, plot controls and expandable geometry details. Inference failures link to Settings; errors remain visible above the active content.

Profiles default to the arithmetic mean of the 10 samples nearest the specified CAX, with 5 samples also available. Equal-distance ties select the lower coordinate. The count must be available, CAX must lie inside the curve, and selected samples must include both sides of CAX. No partial window or fallback normalization is used. On the current 2 mm grid, the nearest 10 bins are centred at −9 through +9 mm. PDD independently defaults to maximum normalization. Alternative profile modes are maximum, CAX, at position and raw summed Gy; PDD offers maximum, at position and raw Gy. Normalization scales dose and absolute uncertainty by the same fixed factor.

CAX/reference interpolation is linear and never extrapolates. Missing or non-positive normalization references produce a visible error for the curve. Sample tooltips show position and dose; visible sample markers are optional. Custom horizontal bounds control the displayed interval; the 95–105% toggle expands the relative-dose plateau. Metrics use the complete curve.
Choose a selected run as reference; the active X/Y/PDD role selects its matching scorer to display differences against compatible-role curves at their sample positions, using linear interpolation only over overlapping coordinates. Differences are Gy or percentage points according to normalization. Export analysed curves and metric tables as CSV.

Uncertainty bands scale the absolute dose uncertainty by the same fixed normalization factor. They do not include uncertainty in the normalization denominator or correlations. Metric uncertainties are not estimated. Raw summed Gy must not be interpreted as Gy/MU or an output factor without matching source-history/weight conventions.

## Metrics and conventions

Flatness and symmetry have independent named equation selectors in Settings. Both initially use the IEC/Elekta ratio. Browser-local defaults include both equation choices, profile/PDD normalization modes and central sample count; older generic ratio/difference defaults migrate to the corresponding equations.

| Metric / selector | Equation |
|---|---|
| Flatness · IEC / Elekta — max/min | `100 * Dmax / Dmin` |
| Flatness · Varian / IAEA — difference | `100 * (Dmax-Dmin)/(Dmax+Dmin)` |
| Symmetry · IEC / Elekta — point ratio | `100 * max(D(x)/D(2*CAX-x), D(2*CAX-x)/D(x))` |
| Symmetry · Varian — point difference | `100 * max(abs(D(x)-D(2*CAX-x)))/D(CAX)` |
| Symmetry · IAEA — area difference | `100 * (AL-AR)/(AL+AR)` |

Sources:

- [Standard Imaging DoseView manual](https://static.standardimaging.com/referenced-manuals/80614-11-DoseView-MNL.pdf), Table 16-2: Varian CAT difference, Elekta photon ratios, and separate Elekta electron definitions.
- [IBA myQA Daily documentation](https://helpcenter.iba-dosimetry.com/hc/en-us/articles/28114820786844-myQA-Daily-2D-Detector-Array): IEC 60976 original mirrored-point ratio and max/min flatness.
- [IAEA Radiation Oncology Physics handbook](https://international.anl.gov/training/materials/IAEA%20Publications/P1196_Handbook%20RADIATION%20ONCOLOGY%20PHYSICS.pdf), §6.9.1–6.9.2, equations 6.64–6.65: normalized flatness difference and signed area symmetry.

These labels identify equations, not complete acceptance protocols. The common max/min IEC/Elekta flatness name refers to the photon convention. DoseView separately lists Elekta IEC electron max/CAX and Elekta USA electron twice-standard flatness, with different regions. Neither electron variant is implemented as a preset. The app does not verify protocol-specific acquisition depth, SSD, spatial averaging or reference dose and does not assign clinical tolerances.

IAEA area symmetry integrates piecewise-linear dose from CAX to the nearest surrounding 50% **CAX-dose** crossings, independently of the point-metric region fraction. Positive means larger left area. Missing crossings or non-positive total area give an unavailable value.
The default evaluation fraction is 0.8 of the symmetric half-field limited by the nearer 50% edge about the specified CAX. It equals the central 80% of FWHM for a centred field. The UI displays this convention; no automatic recentering hides offsets. Mirrored sample knots and region boundaries are interpolated before evaluating extrema. Non-positive denominators give unavailable values.

Profile results include FWHM, centre from outer 50% crossings, and separate left/right 20–80% penumbras. PDD results include sampled dmax and the first distal R90/R80/R50 crossing after that maximum. Ranges are interpolated from dose samples and are not ionization I50 conversions. Insufficient depth coverage yields unavailable values. No smoothing is applied and sampling resolution remains visible. No clinical pass/fail tolerances are assigned. Metric tables retain dataset rows with em dashes when profile calculations are unavailable.

## Validation

`AnalysisTests.fs` checks analytic range crossings, 100% ideal profiles, exact 100.3% ratio examples, difference definitions, normalization and uncertainty scaling, five/ten-point CAX means, signed area integration, unconfirmed mappings, current generated-input fixtures, incompatible bin counts, rotated-geometry fallback, server input lookup, saved overrides and dataset path isolation.

The fixture combines the current water-tank, PDD and X-profile templates from `emintavlayan/tsebt-monte-carlo-notes-and-decisions`, revision `5f0d278d4342a90778388197d418f9c96eb28c36`, substituting the output stem only.

Measured-data import, automatic slices from 3D scorers, gamma and uncertainty propagation for derived metrics are subsequent extensions.

## Implementation boundaries and source links

- [AnalysisOperation.fs](../src/Server/AnalysisOperation.fs) discovers exact `dose_with_uncertainty.csv` filenames recursively, ordered by descending relative ID. It does not filter on SQLite collection status. A file left by an unsuccessful collection may therefore be listed; verify its provenance.
- Loading requires named dose/uncertainty columns, at least two samples, finite numeric values, non-negative doses/uncertainties and unique coordinates along the selected axis. Files larger than 50,000,000 bytes are rejected. More than one varying coordinate axis is rejected.
- [AnalysisInput.fs](../src/Server/AnalysisInput.fs) reads parameter assignments from generated text; last overrides win. It is a restricted parser, not a TOPAS interpreter: it does not expand IncludeFile statements or arithmetic expressions. Direct numeric references have a recursion limit. Length literals support mm/cm/m/um; degrees/radians are recognized for rotation checks.
- Scorer matching is case-sensitive and expects an OutputFile ending in an underscore plus the collected folder name. This is narrower than Collect's arbitrary suffix discovery. `default` cannot be automatically identified. Bin counts must be explicitly available as the scorer axis Bins parameter and equal sample count.
- Inferred X/Y positions accumulate unrotated geometry translations with CAX initialized to 0 mm. PDD requires a direct `G4_WATER` parent and measures depth from its negative-Z face. The parser does not derive SSD, field-size labels or beam-axis displacement from gantry/source definitions.
- Explicit mapping validation requires a recognized role, axis 0–2, finite origin/CAX, finite non-zero spacing and Confirmed=true. It does not prove the mapping matches the physical experiment. Sidecar errors are surfaced as load errors; they are not silently replaced by inferred geometry.
- [Analysis.fs](../src/Shared/Analysis.fs) uses linear segment crossings. For noisy/multi-peaked profiles, FWHM uses first/last 50% crossings; left penumbra uses the first qualifying crossing and right penumbra the last. There is no monotonic-fit/noise rejection. If two edges or a valid symmetric region are absent, the profile metric list can be empty rather than containing an unavailable row per metric.
- [AnalysisViews.fs](../src/Client/AnalysisViews.fs) groups metrics and reference comparisons by the assigned role; the X/Y/PDD buttons select the displayed role. Run/collection grouping uses the output path; standard timestamp folders sort lexically with newest first, including historical direct collection CSVs. A selected run loads all discovered scorers in that collection; scorer load errors are reported individually. [AnalysisPlot.fs](../src/Client/AnalysisPlot.fs) renders the responsive SVG overlay, grid and legend. Matching role does not validate equal SSD, field size, depth, histories or weights. Differences use overlap without propagated uncertainty. Plot limits do not limit metrics; curve export includes all processed samples, not only the plotted interval. Metrics and visible plot values use one decimal place, with scientific notation for small nonzero values. Exports retain six significant digits.
- Manual geometry edits are separate drafts. The active mapping and analysis stay unchanged until the server validates and saves an override. Cancel discards the draft. The persisted `Confirmed` flag remains an internal validity field for sidecar compatibility; it is not a confirmation step for inferred geometry. API failures appear in the workspace; normalization failures identify the affected run and role. Browser-local formula and normalization settings persist, but the selected curve list is not a saved analysis session.

For operator examples, see the [analysis guide](analysis-guide.md).
