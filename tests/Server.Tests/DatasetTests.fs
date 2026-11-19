module Server.Tests.DatasetTests

open System
open System.IO
open System.Text
open Xunit
open Shared
open Server.Tests.TestHelpers

/// Encodes a synthetic original without changing line endings or byte order.
let private file name (text: string) : ImportFile = {Name=name;ContentBase64=Convert.ToBase64String(Encoding.UTF8.GetBytes text)}

/// Builds a reviewed dose import with explicit units and orientation.
let private request () : SaveDatasetRequest =
    let parsed=assertOk(CurveImport.preview [file "measurement.csv" "position_cm,dose_cGy\r\n-1,100\r\n0,200\r\n1,100\r\n"])
    let curves=parsed.Curves |> List.map (fun c -> {c with Metadata={c.Metadata with Role="X profile"}})
    {Name="Measured 36 cm";Source="Measurement";AcquisitionDate="2026-11-02";Confirmed=true;Preview={parsed with Curves=curves}}

/// Runs storage checks in an isolated temporary database.
let private inDatabase check =
    let folder=Path.Combine(Path.GetTempPath(),"saved-dataset-"+Guid.NewGuid().ToString("N"))
    try check (buildSettings folder)
    finally cleanupTestDirectory folder

/// Checks numeric samples and provenance remain independent of client-supplied preview samples.
[<Fact>]
let ``Reviewed imports archive exact bytes and reparse source values`` () = inDatabase (fun settings ->
    let reviewed=request()
    let forged=reviewed.Preview.Curves |> List.map (fun c -> {c with Points=[{Position=999.;Dose=999.;Uncertainty=999.}]})
    let saved=assertOk(DatasetOperation.import settings {reviewed with Preview={reviewed.Preview with Curves=forged}})
    let loaded=assertOk(DatasetStore.load settings saved.Id)
    let curve=loaded.Curves.Head
    Assert.Equal(3,curve.Points.Length)
    Assert.Equal(200.,curve.Points[1].Dose)
    Assert.Equal(10.,curve.Mapping.StepMm)
    Assert.Equal(-1.,curve.Points[0].Position)
    Assert.False(curve.HasUncertainty)
    Assert.NotEmpty(curve.Provenance.Corrections)
    let archived=assertOk(DatasetStore.original settings {DatasetId=saved.Id;FileIndex=0})
    Assert.Equal(reviewed.Preview.Files.Head.ContentBase64,archived.ContentBase64)
    let analysis=assertOk(DatasetStore.loadAnalysisCurve settings saved.Curves.Head.Id)
    Assert.Equal("cGy",analysis.ValueUnit)
    Assert.True(analysis.Mapping.Value.Confirmed))

/// Checks review omissions and malformed scans cannot create partially saved datasets.
[<Fact>]
let ``Incomplete metadata and unconfirmed dose imports are rejected atomically`` () = inDatabase (fun settings ->
    let reviewed=request()
    Assert.True(Result.isError(DatasetOperation.import settings {reviewed with Confirmed=false}))
    let unknown=reviewed.Preview.Curves |> List.map (fun c -> {c with Metadata={c.Metadata with PositionUnit=""}})
    Assert.True(Result.isError(DatasetOperation.import settings {reviewed with Preview={reviewed.Preview with Curves=unknown}}))
    Assert.Empty(assertOk(DatasetStore.list settings))
    for invalid in ["0,1\n0,2";"0,1\n1,NaN";"0,1\n1,-1";"0,1\n1,2,0.1";"Scan ID 1\n0,1\n1,2"] do
        Assert.True(Result.isError(CurveImport.preview [file "bad.txt" invalid])))

/// Checks separate imports remain separate and stale edits cannot overwrite metadata.
[<Fact>]
let ``Saved versions use independent samples revision checks and cascading deletion`` () = inDatabase (fun settings ->
    let first=assertOk(DatasetOperation.import settings (request()))
    let second=assertOk(DatasetOperation.import settings (request()))
    Assert.NotEqual<string>(first.Id,second.Id)
    let loaded=assertOk(DatasetStore.load settings first.Id)
    let edit:UpdateDatasetRequest = {
        Id=first.Id;Revision=first.Revision;Name="Renamed";Source=first.Source;AcquisitionDate=first.AcquisitionDate
        Curves=loaded.Curves |> List.map (fun c -> c.Id,{c.Metadata with DepthMm="13"})
    }
    assertOk(DatasetOperation.update settings edit) |> ignore
    Assert.True(Result.isError(DatasetOperation.update settings edit))
    Assert.True(Result.isError(DatasetStore.delete settings {Id=first.Id;Revision=first.Revision}))
    let updated=assertOk(DatasetStore.load settings first.Id)
    Assert.Equal("13",updated.Curves.Head.Metadata.DepthMm)
    Assert.Equal<AnalysisPoint list>(loaded.Curves.Head.Points,updated.Curves.Head.Points)
    assertOk(DatasetStore.delete settings {Id=first.Id;Revision=updated.Summary.Revision}) |> ignore
    Assert.True(Result.isError(DatasetStore.original settings {DatasetId=first.Id;FileIndex=0}))
    Assert.True(Result.isError(DatasetStore.loadAnalysisCurve settings first.Curves.Head.Id))
    Assert.Single(assertOk(DatasetStore.list settings)) |> ignore
    Assert.Equal(second.Id,(assertOk(DatasetStore.list settings)).Head.Id))

/// Checks multiple bounded scans remain separate and uncertain vendor columns are rejected.
[<Fact>]
let ``MCC blocks preserve scan identities and require explicit units`` () =
    let text="BEGIN_SCAN 1\nSCAN_CURVETYPE=PDD\nBEGIN_DATA\n0 1\n1 2\nEND_DATA\nEND_SCAN 1\nBEGIN_SCAN 2\nSCAN_CURVETYPE=CROSSPLANE_PROFILE\nBEGIN_DATA\n-1 1\n1 2\nEND_DATA\nEND_SCAN 2\n"
    let parsed=assertOk(CurveImport.preview [file "synthetic.mcc" text])
    Assert.Equal(2,parsed.Curves.Length)
    Assert.Equal("1",parsed.Curves[0].Provenance.ScanId)
    Assert.Equal("2",parsed.Curves[1].Provenance.ScanId)
    Assert.Equal("",parsed.Curves[0].Metadata.PositionUnit)
    Assert.Equal("",parsed.Curves[0].Metadata.ValueUnit)
    Assert.True(Result.isError(CurveImport.preview [file "third-column.mcc" (text.Replace("0 1","0 1 3"))]))
    Assert.True(Result.isError(CurveImport.preview [file "truncated.mcc" (text.Replace("END_SCAN 2", ""))]))

/// Checks a collected snapshot survives removal of original output files and is registered once.
[<Fact>]
let ``Historical collection registration is idempotent and snapshots the original curve`` () = inDatabase (fun settings ->
    let folder=Path.Combine(settings.AppRoot,"outputs","1113","20261102T100000","xprofile")
    Directory.CreateDirectory folder |> ignore
    let csv=Path.Combine(folder,"dose_with_uncertainty.csv")
    File.WriteAllText(csv,"x,y,z,dose_to_medium_Gy,standard_uncertainty_Gy\n0,0,0,1,0.01\n1,0,0,2,0.02\n")
    let saved=assertOk(DatasetOperation.registerCollection settings "1113/20261102T100000")
    let repeated=assertOk(DatasetOperation.registerCollection settings "1113/20261102T100000")
    Assert.Equal(saved.Id,repeated.Id)
    File.Delete csv
    let curve=assertOk(DatasetStore.loadAnalysisCurve settings saved.Curves.Head.Id)
    Assert.Equal(2,curve.Points.Length)
    Assert.False(curve.Mapping.Value.Confirmed)
    assertOk(DatasetStore.saveMapping settings {Id=curve.Dataset.Id;Mapping={curve.Mapping.Value with Confirmed=true;OriginMm = -1.;StepMm=2.}}) |> ignore
    let changed=assertOk(DatasetStore.load settings saved.Id)
    Assert.NotEmpty(changed.Curves.Head.Provenance.Corrections)
    Assert.Equal(2,changed.Summary.Revision)
    Assert.True(Result.isError(DatasetStore.loadAnalysisCurve settings "../../secret.csv")))

/// Checks SNC dose-table metadata and cm coordinates without reapplying vendor processing.
[<Fact>]
let ``SNC dose table retains original dose values and explicit geometry`` () =
    let text="Tab-Delimited Scan Output\nFile Scan Count\t1\nBEGIN SCAN\nIs PDI to PDD\tTrue\nBEGIN DOSE TABLE\nAction\tNormalize\n\tX (cm)\tY (cm)\tZ (cm)\tRelative Dose (%)\n\t0\t0\t0\t81.5\n\t0\t0\t1.3\t100\nEND DOSE TABLE\nScan Id\t20641\nScan Date\t03/24/2026 10:22\nScan Type\tDepth Scan\nSource to Surface Distance (cm)\t100\nField Size X (cm)\t40\nField Size Y (cm)\t40\nOffset Axis\t\u0000\nEND SCAN\n"
    let curve=(assertOk(CurveImport.preview [file "pdd.snctxt" text])).Curves.Head
    Assert.Equal("PDD",curve.Metadata.Role)
    Assert.Equal("cm",curve.Metadata.PositionUnit)
    Assert.Equal("%",curve.Metadata.ValueUnit)
    Assert.Equal("1000",curve.Metadata.SsdMm)
    Assert.Equal("400",curve.Metadata.FieldXmm)
    Assert.Equal("2026-03-24T10:22:00",curve.Metadata.AcquisitionDate)
    Assert.Equal(1.3,curve.Points[1].Position)
    Assert.Equal(100.,curve.Points[1].Dose)
    Assert.False(curve.HasUncertainty)
    Assert.True(Result.isError(CurveImport.preview [file "ionization.snctxt" (text.Replace("Is PDI to PDD\tTrue","Is PDI to PDD\tFalse"))]))
    Assert.True(Result.isError(CurveImport.preview [file "truncated.snctxt" (text.Replace("END SCAN", ""))]))

/// Checks MCC metadata wins over misleading filenames without inventing dose units.
[<Fact>]
let ``MCC CC export reads scan geometry and leaves quantity unconfirmed`` () =
    let text="BEGIN_SCAN_DATA\nFORMAT=CC-Export V1.9\nBEGIN_SCAN 1\nSCAN_CURVETYPE=CROSSPLANE_PROFILE\nSSD=1000\nSCAN_DEPTH=0\nFIELD_CROSSPLANE=200\nFIELD_INPLANE=200\nMEAS_DATE=30-Oct-2026 13:50:12\nBEGIN_DATA\n-1 0.9\n1 1\nEND_DATA\nEND_SCAN 1\nEND_SCAN_DATA\n"
    let curve=(assertOk(CurveImport.preview [file "10x10.mcc" text])).Curves.Head
    Assert.Equal("X profile",curve.Metadata.Role)
    Assert.Equal("mm",curve.Metadata.PositionUnit)
    Assert.Equal("200",curve.Metadata.FieldXmm)
    Assert.Equal("0",curve.Metadata.DepthMm)
    Assert.Equal("",curve.Metadata.ValueUnit)
    Assert.Equal("2026-10-30T13:50:12",curve.Metadata.AcquisitionDate)
