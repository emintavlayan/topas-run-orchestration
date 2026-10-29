module Server.Tests.AnalysisTests

open Xunit
open Shared
open Analysis
open Server.Tests.TestHelpers

/// Builds dose samples with fixed absolute uncertainty.
let private curve pairs : AnalysisPoint list = pairs |> List.map(fun (x,d) -> {Position=x;Dose=d;Uncertainty=0.01})

/// Checks interpolation and range calculations against an analytic piecewise-linear PDD.
[<Fact>]
let ``PDD distal ranges interpolate after peak without extrapolation`` () =
    let points=curve [0.,0.5;10.,1.;20.,0.5;30.,0.]
    Assert.Equal(Some 20., distalRange 0.5 points)
    Assert.Equal(Some 14., distalRange 0.8 points)
    Assert.Equal(Some 12., distalRange 0.9 points)
    Assert.Equal(None, interpolate points 31.)
    Assert.Equal(None, distalRange 0.5 (curve [0.,0.5;10.,1.;15.,0.8]))

/// Checks ratio metrics report an ideal 100 percent for a symmetric flat profile.
[<Fact>]
let ``Ratio flatness and symmetry have 100 percent baseline`` () =
    let values=profileMetrics true 0.8 0. (curve [-10.,0.;-8.,1.;0.,1.;8.,1.;10.,0.]) |> Map.ofList
    Assert.Equal(Some 100.,values["Flatness (%)"])
    Assert.Equal(Some 100.,values["Symmetry (%)"])
    Assert.Equal(Some 18.,values["FWHM (mm)"])

/// Checks both selectable definitions against a known asymmetric plateau.
[<Fact>]
let ``Ratio metrics and difference metrics use explicit distinct definitions`` () =
    let points=curve [-10.,0.;-8.,1.;-1.,1.;0.,1.;1.,1.003;8.,1.003;10.,0.]
    let ratios=profileMetrics true 0.8 0. points |> Map.ofList
    let differences=profileMetrics false 0.8 0. points |> Map.ofList
    Assert.Equal(100.3,ratios["Flatness (%)"].Value,9)
    Assert.Equal(100.3,ratios["Symmetry (%)"].Value,9)
    Assert.Equal(100.*0.003/2.003,differences["Flatness (%)"].Value,9)
    Assert.Equal(0.3,differences["Symmetry (%)"].Value,9)

/// Checks normalization scales absolute uncertainty and retains raw-dose mode.
[<Fact>]
let ``Normalization preserves dose units in raw mode and refuses missing reference`` () =
    let points=curve [0.,1.;1.,2.;2.,1.]
    let normalized=assertOk(normalize "Maximum" 0. points)
    Assert.Equal(100.,normalized[1].Dose)
    Assert.Equal(0.5,normalized[1].Uncertainty)
    Assert.Equal<AnalysisPoint list>(points,assertOk(normalize "None" 0. points))
    Assert.True(Result.isError(normalize "CAX" 9. points))

/// Checks unknown geometry cannot silently produce distance metrics.
[<Fact>]
let ``Physical mapping requires explicit confirmation and finite spacing`` () =
    let mapping={Role="PDD";Axis=2;StepMm=2.;OriginMm=1.;CaxMm=0.;Confirmed=false}
    Assert.True(Result.isError(validateMapping mapping))
    Assert.True(Result.isOk(validateMapping {mapping with Confirmed=true}))
    Assert.True(Result.isError(validateMapping {mapping with Confirmed=true;StepMm=0.}))

/// Checks generated-input inference against the actual current PDD and X-profile templates.
[<Fact>]
let ``Input geometry resolves current scorers and detects incompatible grids`` () =
    let text=System.IO.File.ReadAllText(System.IO.Path.Combine(__SOURCE_DIRECTORY__,"Fixtures","generated-input-1113.txt"))
    let pdd=assertOk(AnalysisInput.mappingFromText "pdd" 2 25 text)
    Assert.Equal(2.,pdd.StepMm)
    Assert.Equal(1.,pdd.OriginMm)
    Assert.Equal("PDD",pdd.Role)
    let profile=assertOk(AnalysisInput.mappingFromText "xprofile" 0 300 text)
    Assert.Equal(-299.,profile.OriginMm)
    Assert.Equal(2.,profile.StepMm)
    Assert.True(Result.isError(AnalysisInput.mappingFromText "pdd" 2 20 text))
    Assert.True(Result.isError(AnalysisInput.mappingFromText "pdd" 2 25 (text+"\nd:Ge/WaterTank/RotY = 10 deg\n")))

/// Checks server discovery, automatic input lookup, explicit overrides and path isolation together.
[<Fact>]
let ``Analysis loads collected dose and resolves matching batch inputs`` () =
    let folder=System.IO.Path.Combine(System.IO.Path.GetTempPath(),"analysis-"+System.Guid.NewGuid().ToString("N"))
    try
        let settings=buildSettings folder
        let output=System.IO.Path.Combine(folder,"outputs","1113","20261029T120000","pdd")
        let inputs=System.IO.Path.Combine(folder,"inputs","1113")
        System.IO.Directory.CreateDirectory output |> ignore
        System.IO.Directory.CreateDirectory inputs |> ignore
        let fixture=System.IO.File.ReadAllText(System.IO.Path.Combine(__SOURCE_DIRECTORY__,"Fixtures","generated-input-1113.txt"))
        System.IO.File.WriteAllText(System.IO.Path.Combine(inputs,"seed11137_phsp21.txt"),fixture)
        System.IO.File.WriteAllText(System.IO.Path.Combine(output,"collect_manifest.tsv"),"scorer\trunId\nPDD\tseed11137_phsp21\n")
        let csv=System.IO.Path.Combine(output,"dose_with_uncertainty.csv")
        System.IO.File.WriteAllLines(csv, [yield "x,y,z,dose_to_medium_Gy,standard_uncertainty_Gy"; for z in 0..24 -> $"0,0,{z},1,0.01"])
        let datasets=assertOk(AnalysisOperation.listDatasets settings)
        Assert.Single(datasets) |> ignore
        let loaded=assertOk(AnalysisOperation.loadCurve settings datasets.Head.Id)
        Assert.Equal(1.,loaded.Mapping.Value.OriginMm)
        Assert.True(loaded.Mapping.Value.Confirmed)
        Assert.Equal(25,loaded.Points.Length)
        Assert.Contains("inputs/1113",loaded.MappingSource)
        assertOk(AnalysisOperation.saveMapping settings {Id=datasets.Head.Id;Mapping={loaded.Mapping.Value with OriginMm=2.}}) |> ignore
        let overridden=assertOk(AnalysisOperation.loadCurve settings datasets.Head.Id)
        Assert.Equal(2.,overridden.Mapping.Value.OriginMm)
        Assert.True(Result.isError(AnalysisOperation.loadCurve settings "../../secret.csv"))
    finally
        if System.IO.Directory.Exists folder then System.IO.Directory.Delete(folder,true)

/// Checks flatness and symmetry conventions can be selected independently.
[<Fact>]
let ``Independent metric definition selection preserves requested baselines`` () =
    let values=metricsWithDefinitions "X profile" true false 0.8 0. (curve [-10.,0.;-8.,1.;0.,1.;8.,1.;10.,0.]) |> Map.ofList
    Assert.Equal(Some 100., values["Flatness (%)"])
    Assert.Equal(Some 0., values["Symmetry (%)"])
