# Compare dose curves

[README](../README.md) · [All documentation](index.md)

Analyse reads collected results already on the server. You do not need to download, rename or upload them to use this workspace.

## A first comparison

1. Open **Analyse** and click **Refresh outputs**.
2. Select the desired batch/timestamp/scorer entries. Full labels distinguish recollections.
3. Read each geometry source. Automatically resolved mappings use the archived generated inputs; saved explicit overrides take precedence.
4. If a mapping is unconfirmed, verify its role, spacing, origin and CAX, then save it. The editable 2 mm preset is not proof of the actual geometry.
5. Start with **Maximum** normalization to compare shapes.
6. Select a reference dataset for difference curves. Reference and comparison must share a role.
7. Inspect the metric table and export curves or metrics as CSV.

PDD, X and Y are plotted separately. Point hover shows values; plot minimum/maximum fields restrict the displayed physical interval. Metrics use the complete curve, not just the visible interval. Unavailable values mean the data do not support the calculation, not that the metric equals zero. Profile metric rows can be absent when two 50% edges or a valid CAX evaluation interval cannot be established.

## Choose normalization for your question

| Setting | Question it helps answer |
|---|---|
| Maximum | How do the curve shapes compare when each maximum is 100%? |
| CAX | How do lateral profiles compare relative to their specified central axes? |
| At position | How do curves compare relative to a chosen physical position/depth? |
| None | What are the summed doses in Gy? |

A reference outside the sampled range or with non-positive dose produces an error. No extrapolation is used. For PDD, CAX normalization refers to that mapping's numeric CAX position; choose Maximum or At position when that is not the depth you intend.

Normalizing each curve independently removes amplitude differences. Use consistent source histories and weights when interpreting raw-dose/output differences. Gy is not automatically Gy/MU.

## Select the metric conventions

Flatness and symmetry have independent dropdowns. Both initially use **Ratio (ideal 100%)**; selections are saved in this browser as defaults.

| Metric | Ratio setting | Difference setting |
|---|---|---|
| Flatness | `100 × Dmax / Dmin` | `100 × (Dmax−Dmin)/(Dmax+Dmin)` |
| Symmetry | `100 × max(D(x)/D(2CAX−x), inverse)` | `100 × max(abs(D(x)−D(2CAX−x)))/D(CAX)` |
| Ideal result | 100% | 0% |

A ratio result such as **100.3%** therefore expresses a small departure from the ideal ratio. Formula names are explicit; the app does not claim a vendor protocol or assign clinical pass/fail thresholds.

The default evaluation fraction is 0.8. The interval is symmetric about the specified CAX and limited by the nearer 50% edge. This equals the central 80% of FWHM for a centred profile. A displaced CAX changes the region; it is not silently moved to the detected field centre.

## Read the results

| Result | Meaning |
|---|---|
| Sampled dmax | Position of the highest sampled PDD dose; no peak fitting |
| R90, R80, R50 | First distal dose-threshold crossing after the sampled maximum, linearly interpolated |
| FWHM | Distance between outer 50% profile crossings |
| Field centre | Midpoint of those 50% edges |
| Left/right penumbra | Separate 20–80% crossing distances |
| Reference difference | Current curve minus reference, in Gy or percentage points |

R50 is calculated from dose, not converted from ionization I50. Insufficient depth coverage yields unavailable range values. Sampling spacing still limits the information in an interpolated result.

## Uncertainty and export

Bands use the CSV's absolute statistical uncertainty, scaled by the same fixed factor as dose. They do not include uncertainty in the normalization reference or correlation effects. The app does not report uncertainties for R50, symmetry or other derived metrics.

Curve CSV exports include physical coordinates, dose, scaled uncertainty and normalization. Metric exports include formula choices, evaluation fraction and CAX. Keep the source collection paths and mapping sidecars for provenance.

## Supported now and later

Current: 1D collected scorers, supported generated-input geometry inference, explicit overrides, overlays, metrics and CSV export. Multidimensional scorers are rejected rather than implicitly averaged. Input expressions or rotations outside parser support require manual mapping.

Later: measured-data import, 3D slicing, gamma, figure export and derived-metric uncertainty propagation. [Exact implementation and formula references →](analyse-behaviour-spec.md)
