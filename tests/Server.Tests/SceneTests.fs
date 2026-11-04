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