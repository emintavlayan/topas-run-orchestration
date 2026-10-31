# Compare dose curves

[README](../README.md) · [All documentation](index.md)

Analyse reads collected results directly from the server.

## A first comparison

1. Open **Analyse**. Select run IDs in the left sidebar. Each run's colour identifies its curves; there is no separate plot legend. Expand a run (for example **1114**) to see all collection folders, newest first; the latest is selected by default. Use **Refresh** to discover new collections.
2. **Profiles** shows X and Y together: X is solid, Y is dashed. Use **X**, **Y** or **PDD** to isolate a role. Choosing a run loads every scorer from one collection together; changing the collection replaces that run’s curves. Collections are never mixed automatically.
3. Geometry derived from the archived inputs is used immediately, with no confirmation. **Settings → Geometry** shows its source and values. If inference fails, the curve reports **Geometry unavailable**; enter a manual override and **Save override** only when needed. Manual edits remain drafts until saved; **Cancel** keeps the active geometry. A saved override takes precedence over input inference. Use **Settings → Geometry → Use inputs** to restore input-derived geometry; the existing override is backed up only after inference succeeds.
4. Profiles default to **Central mean → 10 points**. PDD defaults to **Maximum**. Change these independently under **Settings → Normalization**.
5. Open **Metrics** for one row per dataset and one column per metric.
6. Choose **Export Curves** or **Export Metrics** from the toolbar.

## Normalization

| Setting | Reference |
|---|---|
| Central mean | Arithmetic mean of the 5 or 10 samples nearest the specified CAX; profiles only |
| Maximum | Each curve's sampled maximum |
| CAX | Interpolated dose at that profile's CAX |
| At position | Interpolated dose at the chosen position/depth in mm |
| None | Raw summed dose in Gy |

Central mean defaults to 10 samples. For the current centred 2 mm profile grid, these are −9, −7, …, +9 mm, matching the earlier mean within ±1 cm. Five points selects the nearest five samples. Equal-distance ties use the lower position. The requested count must exist and include samples on both sides of CAX; CAX must lie within the sampled profile. There is no fallback to fewer samples or another normalization.

If CAX lies outside a profile, the error includes the mapped minimum and maximum. Settings shows the source mapping, stored coordinate range, spacing and origin. For the standard 300-bin profile, bin coordinates 0–299 should map to −299.0…+299.0 mm. A new collection folder alone does not prove the selected mapping is correct.

The normalization modes, central count and equation choices are saved in this browser. A missing or non-positive reference produces an inline error. No extrapolation is used. Normalization removes amplitude differences; use **None** with consistent source histories/weights when comparing raw doses. Gy is not automatically Gy/MU.

## Metric equations

Choose equations independently in **Settings → Metrics**. The initial flatness and symmetry choices are the IEC/Elekta ratios.

| Selector | Equation |
|---|---|
| Flatness · IEC / Elekta — max/min | `100 × Dmax / Dmin` |
| Flatness · Varian / IAEA — difference | `100 × (Dmax−Dmin)/(Dmax+Dmin)` |
| Symmetry · IEC / Elekta — point ratio | `100 × max(DL/DR, DR/DL)` over mirrored pairs |
| Symmetry · Varian — point difference | `100 × max(abs(DL−DR))/D(CAX)` |
| Symmetry · IAEA — area difference | `100 × (AL−AR)/(AL+AR)` |

Point metrics default to an 80% region, symmetric about CAX and limited by the nearer 50% peak-dose edge. This is the central 80% of FWHM for a centred profile. CAX is not moved to the detected field centre. IAEA area symmetry instead integrates from CAX to each surrounding 50% **CAX-dose** edge using trapezoids; its sign is positive when the left area is larger. The point-region percentage does not alter that area calculation.

These names identify equations. They do not select a complete vendor acceptance protocol. Electron protocols specify additional measurement depths, reference doses and regions, and can use different flatness definitions. **Settings → Equations and references** holds the equations and source links. [Detailed conventions and sources →](analyse-behaviour-spec.md#metrics-and-conventions)

## Plot and results

The Plotly toolbar provides zoom, pan, zoom in/out, autoscale, reset axes and PNG download. Drag a rectangle or scroll to zoom; double-click to reset. The plot resizes with its container. Sample and uncertainty toggles preserve the current zoom; changing runs, collections, view, normalization, reference or bounds initializes a fresh chart. Run colours in the sidebar identify curves; X is solid and Y is dashed. No bottom legend is shown.

**Settings → Plot** controls uncertainty bands, sample markers, horizontal bounds and the reference run. Each displayed curve uses the reference scorer with the same role, including both axes in **Profiles**. Choosing a reference enables the **Difference** view; switch it off in the plot toolbar to return to overlays. Only roles with an available reference are plotted in Difference. Differences use only overlapping coordinates and are in Gy or percentage points.

Metrics always use the complete curve. Plot bounds and interactive zoom do not limit metrics or exports. FWHM displays whole millimetres. Other metrics, axis labels and hover values display one decimal place; raw-dose plots below 0.1 Gy use scientific notation. CSV exports retain six significant digits. Missing values appear as **—**.

| Result | Definition |
|---|---|
| dmax | Position of the highest sampled PDD dose |
| R90, R80, R50 | First distal threshold crossing after that maximum, linearly interpolated |
| FWHM [mm] | Distance between outer half-maximum profile crossings |
| Centre | Midpoint of those crossings |
| L/R 20–80 | Separate left/right penumbra distances |

R50 is dose-based, not an I50 conversion. No smoothing or peak fitting is applied. Finite sampling and missing edge/depth coverage limit available metrics.

Uncertainty bands scale the CSV's absolute statistical uncertainty by the same fixed factor as dose. Normalization uncertainty and covariance are not propagated. Export Curves includes role, normalization, central sample count and CAX; Export Metrics includes role, equations, evaluation fraction and CAX. Keep the source collection and mapping sidecars for provenance.

Supported now: collected 1D scorers, generated-input geometry inference, explicit overrides, overlays, differences, metrics and CSV export. Multidimensional scorers require explicit extraction. Measured-data import, 3D slicing, gamma and derived-metric uncertainty propagation remain future work.
