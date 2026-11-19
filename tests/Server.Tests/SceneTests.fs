module Server.Tests.SceneTests

open System
open System.IO
open Xunit
open Shared
open Server.Tests.TestHelpers

/// Represents nested frames, a phantom, a scoring line and an external CAD asset.
let private geometry =
    """
s:Ge/Gantry/Type = "Group"
s:Ge/Gantry/Parent = "World"
d:Ge/Gantry/RotY = 90 deg
s:Ge/Phantom/Type = "TsBox"
s:Ge/Phantom/Parent = "Gantry"
d:Ge/Phantom/HLX = 2 cm
d:Ge/Phantom/HLY = Ge/Phantom/HLX
d:Ge/Phantom/HLZ = 3 cm
s:Ge/Head/Type = "TsCAD"
s:Ge/Head/Parent = "Gantry"
s:Ge/Head/InputFile = "../../cad-sources/head"
s:Ge/Head/FileFormat = "stl"
d:Ge/Head/Units = 1 mm
s:So/PHSP/Type = "PhaseSpace"
s:So/PHSP/Component = "Gantry"
s:Sc/Dose/Quantity = "DoseToMedium"
s:Sc/Dose/Component = "Phantom"
i:Sc/Dose/XBins = 20
"""

/// Checks units, parameter references, source ownership and CAD filename conventions.
[<Fact>]
let ``generated scene retains typed solids and owners`` () =
    let scene = TopasScene.parse geometry |> assertOk
    let phantom = scene.Components |> List.find (fun c -> c.Name = "Phantom")
    Assert.Equal(Box { X = 20.; Y = 20.; Z = 30. }, phantom.Shape)
    Assert.Equal(Some "Gantry", phantom.Parent)
    Assert.Equal("Gantry", scene.Sources.Head.Component)
    Assert.Equal("Phantom", scene.Scorers.Head.Component)
    Assert.Contains(("X", 20), scene.Scorers.Head.Bins)

    Assert.Equal(
        Cad("../../cad-sources/head.stl", 1.),
        (scene.Components |> List.find (fun c -> c.Name = "Head")).Shape
    )

/// Rejects missing ancestry, cyclic placement, invalid dimensions and unresolved includes.
[<Theory>]
[<InlineData("s:Ge/Gantry/Parent = \"World\"", "s:Ge/Gantry/Parent = \"Phantom\"")>]
[<InlineData("s:Ge/Gantry/Parent = \"World\"", "s:Ge/Gantry/Parent = \"Missing\"")>]
[<InlineData("d:Ge/Phantom/HLX = 2 cm", "d:Ge/Phantom/HLX = -2 cm")>]
[<InlineData("d:Ge/Phantom/HLX = 2 cm", "d:Ge/Phantom/HLX = 2 deg")>]
[<InlineData("d:Ge/Phantom/HLX = 2 cm", "d:Ge/Phantom/HLX = Ge/Phantom/HLY")>]
[<InlineData("i:Sc/Dose/XBins = 20", "i:Sc/Dose/XBins = 1.5")>]
let ``invalid geometry is never silently positioned`` (before: string) (after: string) =
    TopasScene.parse (geometry.Replace(before, after)) |> assertError |> ignore

/// Rejects unresolved includes rather than showing an incomplete simulation.
[<Fact>]
let ``viewer requires resolved generated inputs`` () =
    TopasScene.parse ("includeFile = phantom.txt\n" + geometry)
    |> assertError
    |> ignore

/// Preserves slash-containing names and comments inside quoted CAD paths.
[<Fact>]
let ``names use explicit parent relationships`` () =
    let scene =
        TopasScene.parse (
            geometry
                .Replace("Phantom", "A/Phantom")
                .Replace("../../cad-sources/head", "../../cad-sources/head#1")
        )
        |> assertOk

    Assert.Equal(Some "Gantry", (scene.Components |> List.find (fun c -> c.Name = "A/Phantom")).Parent)

    Assert.Equal(
        Cad("../../cad-sources/head#1.stl", 1.),
        (scene.Components |> List.find (fun c -> c.Name = "Head")).Shape
    )

/// Reads the existing commissioned scorer fixture with correct physical dimensions.
[<Fact>]
let ``existing scorer fixture maps to three scoring volumes`` () =
    let path =
        Path.Combine(__SOURCE_DIRECTORY__, "Fixtures", "generated-input-1113.txt")

    let scene = TopasScene.parse (File.ReadAllText path) |> assertOk
    Assert.Equal(3, scene.Scorers.Length)

    Assert.Equal(
        Cylinder(0., 310., 150., 0., 360.),
        (scene.Components |> List.find (fun c -> c.Name = "WaterTank")).Shape
    )

/// Reads a CAD asset only by a component named in a generated input.
[<Fact>]
let ``generated input and asset access stay within workspace`` () =
    let root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"))

    try
        Directory.CreateDirectory(Path.Combine(root, "inputs", "1113")) |> ignore
        Directory.CreateDirectory(Path.Combine(root, "cad-sources")) |> ignore
        File.WriteAllText(Path.Combine(root, "inputs", "1113", "input.txt"), geometry)
        File.WriteAllBytes(Path.Combine(root, "cad-sources", "head.stl"), [| 1uy; 2uy; 3uy |])
        let settings = buildSettings root
        Assert.Single(SceneOperation.listInputs settings |> assertOk) |> ignore
        SceneOperation.load settings "../outside.txt" |> assertError |> ignore

        SceneOperation.loadAsset settings {
            InputId = "1113/input.txt"
            Component = "Phantom"
        }
        |> assertError
        |> ignore

        let asset =
            SceneOperation.loadAsset settings {
                InputId = "1113/input.txt"
                Component = "Head"
            }
            |> assertOk

        Assert.Equal("AQID", asset.Base64)
        SceneOperation.confinedPath root "../outside.stl" |> assertError |> ignore
    finally
        Directory.Delete(root, true)

/// Represents an asymmetric IAEA footprint with a constant scoring-plane Z coordinate.
let private sourceHeader =
    """
$RECORD_CONTENTS:
1 // X
1 // Y
0 // Z
1 // U
1 // V
0 // W
0 // weight
0 // extra floats
0 // extra ints
$RECORD_CONSTANT:
26.7 // Z in cm
$STATISTICAL_INFORMATION_GEOMETRY:
direction min max
x -6.5 6.5
y -4.0 5.0
"""

/// Checks centimetres, asymmetric bounds, ignored Z, scaling and axis inversion.
[<Fact>]
let ``phase space uses measured header bounds and source transforms`` () =
    let bounds =
        PhaseSpaceGeometry.parseBounds { X = -2.; Y = 1.; Z = 0. } sourceHeader
        |> assertOk

    Assert.Equal({ X = -130.; Y = -40.; Z = 0. }, bounds.Min)
    Assert.Equal({ X = 130.; Y = 50.; Z = 0. }, bounds.Max)

    let placed =
        PhaseSpaceGeometry.parseBounds { X = 1.; Y = 1.; Z = 1. } sourceHeader
        |> assertOk

    Assert.Equal(0., placed.Min.Z)
    Assert.Equal(0., placed.Max.Z)

/// Checks unlabeled IAEA ranges and rejects missing, reversed or non-finite bounds.
[<Fact>]
let ``phase space never substitutes illustrative dimensions`` () =
    let scale = { X = 1.; Y = 1.; Z = 0. }

    let anonymous =
        sourceHeader.Replace("direction min max\nx -6.5 6.5\ny -4.0 5.0", "-6.5 6.5\n-4.0 5.0")

    Assert.Equal(PhaseSpaceGeometry.parseBounds scale sourceHeader, PhaseSpaceGeometry.parseBounds scale anonymous)

    PhaseSpaceGeometry.parseBounds scale "$RECORD_LENGTH:\n21\n"
    |> assertError
    |> ignore

    PhaseSpaceGeometry.parseBounds scale (sourceHeader.Replace("x -6.5 6.5", "x 6.5 -6.5"))
    |> assertError
    |> ignore

    PhaseSpaceGeometry.parseBounds scale (sourceHeader.Replace("x -6.5 6.5", "x -1e999 1e999"))
    |> assertError
    |> ignore

/// Checks TOPAS scale precedence over deprecated ignore flags and local inversion.
[<Fact>]
let ``source transforms retain TOPAS precedence`` () =
    let text =
        geometry
        + "\nu:So/PHSP/PhaseSpaceScaleXPosBy = 2\nb:So/PHSP/PhaseSpaceIgnoreXPos = \"true\"\nb:So/PHSP/PhaseSpaceInvertXAxis = \"true\"\nb:So/PHSP/PhaseSpaceIgnoreZPos = \"true\""

    let source = (TopasScene.parse text |> assertOk).Sources.Head
    Assert.Equal({ X = -2.; Y = 1.; Z = 0. }, source.PositionScale)

    TopasScene.parse (text + "\nu:So/PHSP/PhaseSpaceScaleYPosBy = -1")
    |> assertError
    |> ignore

/// Checks one representative per run, deterministic fallback and source-library header resolution.
[<Fact>]
let ``run discovery groups duplicate inputs and reads referenced source headers`` () =
    let root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"))

    try
        let input = Path.Combine(root, "inputs", "1117")
        let run = Path.Combine(root, "runs", "1117")
        Directory.CreateDirectory input |> ignore
        Directory.CreateDirectory run |> ignore
        let settings = buildSettings root

        let text =
            geometry
            + "\ns:So/PHSP/PhaseSpaceFileName = \"source\"\nb:So/PHSP/PhaseSpaceIgnoreZPos = \"true\""

        File.WriteAllText(Path.Combine(input, "a.txt"), "invalid geometry")
        File.WriteAllText(Path.Combine(input, "b.txt"), text)
        File.WriteAllText(Path.Combine(input, "c.txt"), text)
        File.WriteAllText(Path.Combine(run, "source.header"), sourceHeader)
        let inputs = SceneOperation.listInputs settings |> assertOk
        Assert.Single inputs |> ignore
        Assert.Equal("1117", inputs.Head.Batch)
        Assert.Equal("b.txt", inputs.Head.FileName)
        let scene = SceneOperation.load settings inputs.Head.Id |> assertOk
        let bounds = scene.Sources.Head.Bounds |> Option.get
        Assert.Equal(130., bounds.Max.X - bounds.Min.X)
        Assert.Empty scene.Warnings
        File.Delete(Path.Combine(run, "source.header"))
        let unavailable = SceneOperation.load settings inputs.Head.Id |> assertOk
        Assert.True(unavailable.Sources.Head.Bounds.IsNone)
        Assert.Single unavailable.Warnings |> ignore
    finally
        Directory.Delete(root, true)