# Generated-input geometry viewer

[README](../README.md) · [All documentation](index.md)

Open **Geometry**, select a generated input under its batch, and inspect the geometry that was generated for that run. Refresh reloads the file list and selected input. Viewing never starts TOPAS or changes input files.

## Controls

- Drag to orbit; Shift+drag to pan; scroll to zoom.
- Fit resets the camera around the available geometry. Front, Side and Top select standard world views.
- The component tree follows explicit `Parent` relationships. Select a component to see its local placement, material, shape, attached scorers and sources.
- Visibility controls hide a component and its descendants. `Include=False` components stay excluded. Components marked `Invisible` in TOPAS graphics are shown for inspection and identified in their details.
- Local axes use red X, green Y and blue Z. The World frame marks the coordinate origin; it is not assumed to be a clinical isocentre.
- Source frames use a purple square and local +Z guide. Their drawn size is illustrative; the viewer does not read particle records or infer an aperture from a phase-space file.
- Active scoring volumes are red. Box faces and cylinder end faces can show surface overlays. Linear box bin dividers and cylinder Z-bin rings are drawn for up to 512 bins per axis.

## Supported generated inputs

The viewer reads `inputs/<batch>/*.txt` under the configured application root. It supports `Group`, `TsBox`, full/sector/hollow `TsCylinder`, and `TsCAD` STL components. Lengths are normalized to millimetres and rotations to degrees. Parent transforms are composed using intrinsic XYZ rotations without a display mirror.

Numeric literals and direct parameter references are supported, including cycle detection. A repeated assignment uses its last value, matching the generator's stitched-file convention. Scorers retain their volume/surface attachment, quantity, active state and bin counts; component bins are used when a scorer does not override them.

This is a viewer for resolved generated inputs. Unresolved `includeFile` statements, unsupported expressions, missing parents, cyclic relationships, incorrect units and invalid dimensions produce an error instead of guessed placement. Other solid types retain their frame and show a warning. It does not implement TOPAS physics, overlap checks, time features, general geometry editing or arbitrary TOPAS defaults.

## STL assets

`InputFile` and `FileFormat` determine the CAD filename. `Units` determines its scale. Relative CAD paths are resolved from `runs/<batch>`, matching the generated Slurm working directory. Absolute paths are accepted only inside the configured application root. The original STL coordinates are retained.

Missing or unsupported CAD assets produce visible messages while the remaining scene remains available. Linked paths and files outside the workspace are rejected. Limits are 8 MiB per generated input and 32 MiB per STL. ASCII and binary STL are read by JSCAD.

## Implementation and checks

- `Shared/Scene.fs`: renderer-independent shapes, frames, scorer/source attachments and API contracts.
- `Server/TopasScene.fs`: pure TOPAS-to-scene parser using `Result` and FsToolkit.ErrorHandling.
- `Server/SceneOperation.fs`: bounded input discovery and CAD access.
- `Client/SceneViews.fs`: F# UI, request lifetime handling, component inspection and visibility.
- `Client/SceneGeometry.mjs` and `SceneCanvas.jsx`: JSCAD geometry, matrix composition and WebGL lifecycle.

BeamHead is maintained independently. Additional engines can supply their own scene adapters when needed.

Run `dotnet test tests/Server.Tests/Server.Tests.fsproj` and, after installing client dependencies, `node --test tests/Scene/geometry.test.mjs`. The tests cover generated scorer geometry, units and references, parent validation, CAD paths, rotation composition, STL scaling and rendering primitives.
