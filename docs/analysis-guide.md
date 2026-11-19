# Compare dose curves

[README](../README.md) · [All documentation](index.md)

Analyse reads saved measurement and simulation datasets from SQLite. Use [Import](import-guide.md) for measurement files and historical collections. New Collect operations register automatically when their scorers support 1D analysis.

## A first comparison

1. Open **Analyse**. Select runs or measurement datasets in the left sidebar. A checkbox loads/removes the complete dataset; there are no individual profile checkboxes. Each collection version is an independent dataset. Use **Refresh** to update the list.
2. **Profiles** shows X and Y together: X is solid, Y is dashed. Use **X**, **Y** or **PDD** to isolate a role. Selecting several datasets overlays their curves; no versions are silently replaced.
3. **Settings → Geometry** shows the mapping snapshot saved at import/registration. Confirmed mappings are active immediately. If geometry is unavailable, edit and save manual values. Saved overrides live in SQLite and leave raw samples unchanged. **Restore imported geometry** restores the initial confirmed snapshot; it does not rerun input inference.
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
| None | Raw dose in its declared units; cGy converted to Gy |

Central mean defaults to 10 samples. For the current centred 2 mm profile grid, these are −9, −7, …, +9 mm, matching the earlier mean within ±1 cm. Five points selects the nearest five samples. Equal-distance ties use the lower position. The requested count must exist and include samples on both sides of CAX; CAX must lie within the sampled profile. There is no fallback to fewer samples or another normalization.

If CAX lies outside a profile, the error identifies the run/scorer and includes the mapped minimum and maximum. That scorer is omitted; other valid scorers from the same run can remain visible. Click the error to open Settings. Settings shows the source mapping, stored coordinate range, spacing and origin. For the standard 300-bin profile, bin coordinates 0–299 should map to −299.0…+299.0 mm. A new collection folder alone does not prove the selected mapping is correct.

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

The Plotly toolbar provides zoom, pan, zoom in/out, autoscale, reset axes and PNG download. Drag a rectangle or scroll to zoom; double-click to reset. The plot resizes with its container. Sample and uncertainty toggles preserve the current zoom; changing curves, view, normalization or bounds initializes a fresh chart. One colour in the sidebar identifies each run/dataset; X is solid and Y is dashed; X is solid and Y is dashed. No bottom legend is shown.

**Settings → Plot** controls uncertainty bands, sample markers and horizontal bounds. Selected runs are overlaid directly in Profiles, X, Y and PDD views.

Metrics always use the complete curve. Plot bounds and interactive zoom do not limit metrics or exports. FWHM displays whole millimetres. Other metrics, axis labels and hover values display one decimal place; raw-dose plots below 0.1 Gy use scientific notation. CSV exports retain six significant digits. Missing values appear as **—**.

| Result | Definition |
|---|---|
| dmax | Position of the highest sampled PDD dose |
| R90, R80, R50 | First distal threshold crossing after that maximum, linearly interpolated |
| FWHM [mm] | Distance between outer half-maximum profile crossings |
| Centre | Midpoint of those crossings |
| L/R 20–80 | Separate left/right penumbra distances |

R50 is dose-based, not an I50 conversion. No smoothing or peak fitting is applied. Finite sampling and missing edge/depth coverage limit available metrics.

Uncertainty bands scale the CSV's absolute statistical uncertainty by the same fixed factor as dose. Normalization uncertainty and covariance are not propagated. Export Curves includes role, normalization, central sample count and CAX; Export Metrics includes role, equations, evaluation fraction and CAX. Original files, samples, metadata and mapping corrections are archived in SQLite. Missing uncertainty is blank in exports and produces no uncertainty band.

Supported now: saved 1D simulation/measurement curves, input-geometry inference at collection registration, explicit overrides, overlays, metrics and CSV export. See [Import formats and limits](import-guide.md). Multidimensional scorers require explicit extraction. 3D slicing, gamma and derived-metric uncertainty propagation remain future work.

For an incorrect saved mapping, enter manual values under Settings → Geometry, or restore its imported snapshot. To derive geometry again from corrected archived inputs, register a new collection version.
