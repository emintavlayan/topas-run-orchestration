# Compare dose curves

[README](../README.md) · [All documentation](index.md)

Analyse reads collected results directly from the server.

## A first comparison

1. Open **Analyse**. Select outputs in the left panel; use **Filter outputs** or **Refresh** as needed.
2. **Profiles** overlays X/Y in one plot. Use **X**, **Y** or **PDD** to isolate a role. X is solid; Y is dashed. Scorers from one collection share a colour.
3. Open **Settings → Geometry** if a mapping needs confirmation. Check role, spacing, origin and CAX, then **Confirm and save**. Confirmed geometry is collapsed by default.
4. Profiles default to **Central mean → 10 points**. PDD defaults to **Maximum**. Change these independently under **Settings → Normalization**.
5. Open **Metrics** for one row per dataset and one column per metric.
6. Download **Curves CSV** or **Metrics CSV** from the toolbar.

## Normalization

| Setting | Reference |
|---|---|
| Central mean | Arithmetic mean of the 5 or 10 samples nearest the specified CAX; profiles only |
| Maximum | Each curve's sampled maximum |
| CAX | Interpolated dose at that profile's CAX |
| At position | Interpolated dose at the chosen position/depth in mm |
| None | Raw summed dose in Gy |

Central mean defaults to 10 samples. For the current centred 2 mm profile grid, these are −9, −7, …, +9 mm, matching the earlier mean within ±1 cm. Five points selects the nearest five samples. Equal-distance ties use the lower position. The requested count must exist and include samples on both sides of CAX; CAX must lie within the sampled profile. There is no fallback to fewer samples or another normalization.

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

**Settings → Plot** controls uncertainty bands, sample markers, horizontal bounds and the difference reference. Choosing a reference enables the **Difference** view for curves with the same role; switch it off in the plot toolbar to return to overlays. Differences use only overlapping coordinates and are in Gy or percentage points. **95–105%** expands the relative-dose plateau vertically.

Metrics always use the complete curve. Plot bounds do not limit metrics or exports. Missing values appear as **—**.

| Result | Definition |
|---|---|
| dmax | Position of the highest sampled PDD dose |
| R90, R80, R50 | First distal threshold crossing after that maximum, linearly interpolated |
| 50% width | Distance between outer half-maximum profile crossings |
| Centre | Midpoint of those crossings |
| L/R 20–80 | Separate left/right penumbra distances |

R50 is dose-based, not an I50 conversion. No smoothing or peak fitting is applied. Finite sampling and missing edge/depth coverage limit available metrics.

Uncertainty bands scale the CSV's absolute statistical uncertainty by the same fixed factor as dose. Normalization uncertainty and covariance are not propagated. Curves CSV includes role, normalization, central sample count and CAX; Metrics CSV includes role, equations, evaluation fraction and CAX. Keep the source collection and mapping sidecars for provenance.

Supported now: collected 1D scorers, generated-input geometry inference, explicit overrides, overlays, differences, metrics and CSV export. Multidimensional scorers require explicit extraction. Measured-data import, 3D slicing, gamma and derived-metric uncertainty propagation remain future work.
