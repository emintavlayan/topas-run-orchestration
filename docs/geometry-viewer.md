# Generated-input geometry viewer

[README](../README.md) · [All documentation](index.md)

Open **Geometry**, select a run ID, and inspect the geometry that was generated for that run. The first available run opens automatically. Each run appears once; the viewer uses its first readable generated input in filename order. Refresh preserves the selected run when possible. Viewing never starts TOPAS or changes input files.

## Controls

- Drag to orbit; right-drag or Shift+drag to pan; scroll to zoom. The camera stays in place while selecting components or toggling visibility.
- **Fit all** frames visible geometry. **Focus selected** frames the selected component and its descendants. Double-click a mesh, PHSP footprint or component name in the tree to focus it directly. Front, Side and Top select standard world views; the clickable view cube is labelled with the original TOPAS axes.
- Choose **Solid**, **Transparent** or **Wireframe** display. **Isolate selected** shows one subtree; **Show all** restores visibility.
- Click a mesh or select it in the component tree, which follows explicit `Parent` relationships. Select a component to see its local placement, material, shape, attached scorers and sources.
- Visibility controls hide a component and its descendants. `Include=False` components stay excluded. Components marked `Invisible` in TOPAS graphics are shown for inspection and identified in their details.
- Local axes use red X, green Y and blue Z. The World frame marks the coordinate origin; it is not assumed to be a clinical isocentre.
- Phase-space sources show purple bounds from the referenced IAEA/Varian header’s `$STATISTICAL_INFORMATION_GEOMETRY` section, converted from cm to mm. Bounds retain asymmetric offsets and follow source position scaling, ignore flags, axis inversion and parent transforms. Limited records without stored Z use local Z=0, matching TOPAS; the header’s constant Z is not added a second time. Local +Z guides remain orientation indicators. Missing bounds produce a warning and no substitute square.
- Active scoring volumes are red. Box faces and cylinder end faces can show surface overlays. Linear box bin dividers and cylinder Z-bin rings are drawn for up to 512 bins per axis.

## Supported generated inputs

The viewer reads `inputs/<batch>/*.txt` under the configured application root. It supports `Group`, `TsBox`, full/sector/hollow `TsCylinder`, and `TsCAD` STL components. Lengths are normalized to millimetres and rotations to degrees. Parent transforms are composed using intrinsic XYZ rotations in physical coordinates. A final display-only Z reflection matches BeamHead: TOPAS +Z points downward. Meshes, source/scorer guides and axes share this reflection; input values and geometry relationships remain in their original coordinates.

Numeric literals and direct parameter references are supported, including cycle detection. A repeated assignment uses its last value, matching the generator's stitched-file convention. Scorers retain their volume/surface attachment, quantity, active state and bin counts; component bins are used when a scorer does not override them.

This is a viewer for resolved generated inputs. Unresolved `includeFile` statements, unsupported expressions, missing parents, cyclic relationships, incorrect units and invalid dimensions produce an error instead of guessed placement. Other solid types retain their frame and show a warning. It does not implement TOPAS physics, overlap checks, time features, general geometry editing or arbitrary TOPAS defaults.

## STL assets

`InputFile` and `FileFormat` determine the CAD filename. `Units` determines its scale. Relative CAD paths are resolved from `runs/<batch>`, matching the generated Slurm working directory. Absolute paths are accepted only inside the configured application root. The original STL coordinates are retained.

Missing or unsupported CAD assets produce visible messages while the remaining scene remains available. Linked paths and files outside the workspace are rejected. Limits are 8 MiB per generated input and 32 MiB per STL. ASCII and binary STL are read directly by Three.js.

## Phase-space headers

Relative `PhaseSpaceFileName` paths resolve from `runs/<batch>`. Absolute source-library paths are supported. The viewer reads only the referenced `.header` (or `.IAEAheader`) file, up to 1 MiB. It does not scan large particle files. Headers without coordinate bounds, including standard TOPAS ASCII/Binary headers, currently have no footprint. Header ranges are the precision reported by the producer, not a reconstructed aperture or particle-density map.

TOPAS source-position semantics follow [TsSourcePhaseSpace.cc](https://github.com/OpenTOPAS/OpenTOPAS/blob/main/primary/TsSourcePhaseSpace.cc). In particular, an explicit `PhaseSpaceScale*PosBy` takes precedence over the deprecated `PhaseSpaceIgnore*Pos` flag.

## Implementation and checks

- `Shared/Scene.fs`: renderer-independent shapes, frames, scorer/source attachments and API contracts.
- `Server/TopasScene.fs`: pure TOPAS-to-scene parser using `Result` and FsToolkit.ErrorHandling.
- `Server/PhaseSpaceGeometry.fs`: pure header bounds parser.
- `Server/SceneOperation.fs`: one representative input per run, source-header resolution and bounded CAD access.
- `Client/SceneViews.fs`: F# UI, request lifetime handling, component inspection and visibility.
- `Client/SceneGeometry.mjs`: Three.js primitives/STL, physical transforms, visibility and camera framing.
- `Client/SceneCanvas.jsx`: React Three Fiber canvas with Drei orbit controls, view cube, picking and materials.

BeamHead is maintained independently. Additional engines can supply their own scene adapters when needed.

Run `dotnet test tests/Server.Tests/Server.Tests.fsproj` and, after installing client dependencies, `node --test tests/Scene/geometry.test.mjs`. The tests cover generated scorer geometry, units and references, parent validation, CAD paths, rotation composition, STL scaling and rendering primitives.


The browser regression check starts the built server and Vite against temporary inputs; ports 5000 and 8080 must be free. It checks automatic run selection, one entry per run, refresh preservation, repeated tree/mesh/PHSP double-click focus, source dimensions, transparent picking and mobile overflow. After building the server and compiling Fable, install Playwright in a temporary tools directory and run:

```sh
npm install --prefix /tmp/topas-viewer-tools playwright
/tmp/topas-viewer-tools/node_modules/.bin/playwright install chromium --only-shell
NODE_PATH=/tmp/topas-viewer-tools/node_modules node tests/Scene/viewer.browser.cjs
```
