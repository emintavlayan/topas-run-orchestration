# Analyse collected outputs

[README](../README.md) · [All documentation](index.md)

Analyse reads final `dose_with_uncertainty.csv` files from the configured output root. The CSV's dose and standard uncertainty are both in Gy. Coordinate columns retain TOPAS bin coordinates: Collect does not convert them to millimetres.

## Geometry detection

For `outputs/1113/{timestamp}/pdd/dose_with_uncertainty.csv`, the scorer manifest identifies retained run IDs. Analyse reads their generated `inputs/1113/{runId}.txt` files, matches the scorer OutputFile suffix, and resolves its Component, bin count, half-length, translations and parent chain. All contributing inputs must agree. Current unrotated TsBox X/Y profiles and water-parent TsCylinder/TsBox PDD scorers are supported. Literal mm/cm/m/um values and direct parameter references are resolved; expressions and rotated ancestry fall back to explicit mapping.

For the current water-tank templates:

- PDD: 25 bins over 50 mm; depth centres 1, 3, …, 49 mm from the water entrance face.
- X/Y profiles: 300 bins over 600 mm; centres −299, −297, …, +299 mm.

Input files are the archived generated inputs, not current templates. Missing inputs, inconsistent geometry, mismatched bin counts and ambiguous scorers do not enable inferred distance metrics. The interface shows the reason and offers an unconfirmed editable preset. User-confirmed overrides are atomically saved beside the CSV as `.analysis.json`; the CSV is unchanged. Saved overrides take precedence. Historical manually renamed/default scorer collections require explicit mapping. Multidimensional scorers are rejected rather than averaged implicitly.

## Curve processing

Select multiple datasets and keep their full collection/scorer labels. PDD, X and Y are plotted separately. Normalization is raw summed Gy, individual maximum, CAX, or a specified physical position. CAX/reference interpolation is linear and never extrapolates. Missing or non-positive normalization references produce a visible error for the curve. Point markers show physical position and dose on hover. Plot bounds control the displayed physical interval; metrics use the complete curve.

Choose a selected dataset as reference to display differences against compatible-role curves at their sample positions, using linear interpolation only over overlapping coordinates. Differences are Gy or percentage points according to normalization. Export analysed curves and metric tables as CSV.

Uncertainty bands scale the absolute dose uncertainty by the same fixed normalization factor. They do not include uncertainty in the normalization denominator or correlations. Metric uncertainties are not estimated. Raw summed Gy must not be interpreted as Gy/MU or an output factor without matching source-history/weight conventions.

## Metrics and conventions

Defaults for both flatness and symmetry are ratio definitions with an ideal value of 100%. Each formula can be selected independently, and browser-local selections become the defaults on the next visit.

- Flatness ratio: `100 * Dmax / Dmin` within the evaluation region.
- Symmetry ratio: `100 * max(D(x)/D(2*CAX-x), D(2*CAX-x)/D(x))` within the region.
- Flatness difference: `100 * (Dmax-Dmin)/(Dmax+Dmin)`, ideal zero.
- Symmetry absolute point difference: `100 * max(abs(D(x)-D(2*CAX-x)))/D(CAX)`, ideal zero.

No vendor name is assigned to these definitions. Primary software documentation describes the alternative conventions: [Pylinac profile metric definitions](https://pylinac.readthedocs.io/en/latest/topics/profiles.html)

The default evaluation fraction is 0.8 of the symmetric half-field limited by the nearer 50% edge about the specified CAX. It equals the central 80% of FWHM for a centred field. The UI displays this convention; no automatic recentering hides offsets. Mirrored sample knots and region boundaries are interpolated before evaluating extrema. Non-positive denominators give unavailable values.

Profile results include FWHM, centre from outer 50% crossings, and separate left/right 20–80% penumbras. PDD results include sampled dmax and the first distal R90/R80/R50 crossing after that maximum. Ranges are interpolated from dose samples and are not ionization I50 conversions. Insufficient depth coverage yields unavailable values. No smoothing is applied and sampling resolution remains visible. No clinical pass/fail tolerances are assigned.

## Validation

`AnalysisTests.fs` checks analytic range crossings, 100% ideal profiles, exact 100.3% ratio examples, difference definitions, normalization and uncertainty scaling, unconfirmed mappings, current generated-input fixtures, incompatible bin counts, rotated-geometry fallback, server input lookup, saved overrides and dataset path isolation.

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
- [AnalysisViews.fs](../src/Client/AnalysisViews.fs) groups by the assigned role only. Matching role does not validate equal SSD, field size, depth, histories or weights. Differences use overlap without propagated uncertainty. Plot limits do not limit metrics; curve export includes all processed samples, not only the plotted interval. Numbers are formatted to six significant digits in the current exports.
- Changing a mapping marks it unconfirmed until saved. API failures appear in the workspace; normalization failures appear beside the affected curve. Browser-local formula settings persist, but the selected curve list is not a saved analysis session.

For operator examples, see the [analysis guide](analysis-guide.md).
