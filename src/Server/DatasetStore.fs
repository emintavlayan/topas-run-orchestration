module DatasetStore

open System
open System.IO
open System.Text.Json
open Microsoft.Data.Sqlite
open Shared
open TsebtConfig
open FsToolkit.ErrorHandling

/// Creates a parameterized SQLite command, optionally inside a transaction.
let private command (connection: SqliteConnection) (transaction: SqliteTransaction option) sql parameters =
    let cmd = connection.CreateCommand()
    transaction |> Option.iter (fun value -> cmd.Transaction <- value)
    cmd.CommandText <- sql
    for name,value in parameters do cmd.Parameters.AddWithValue(name,value) |> ignore
    cmd

/// Executes a parameterized mutation and returns the affected row count.
let private execute connection transaction sql parameters =
    use cmd = command connection transaction sql parameters
    cmd.ExecuteNonQuery()

/// Opens the configured database with foreign-key checks enabled.
let private connect (settings: TsebtSettings) =
    let path = Bootstrap.combineAppRoot settings.AppRoot settings.Paths.Database
    Directory.CreateDirectory(Path.GetDirectoryName path) |> ignore
    let builder = SqliteConnectionStringBuilder()
    builder.DataSource <- path
    builder.ForeignKeys <- Nullable true
    let connection = new SqliteConnection(builder.ConnectionString)
    connection.Open()
    connection

/// Defines additive tables for saved datasets and original files.
let private schema = """
CREATE TABLE IF NOT EXISTS saved_datasets (
 id TEXT PRIMARY KEY, name TEXT NOT NULL, source TEXT NOT NULL, acquired_at TEXT NOT NULL,
 saved_at TEXT NOT NULL, collection_key TEXT NOT NULL, revision INTEGER NOT NULL DEFAULT 1);
CREATE UNIQUE INDEX IF NOT EXISTS idx_saved_collection ON saved_datasets(collection_key) WHERE collection_key <> '';
CREATE TABLE IF NOT EXISTS saved_curves (
 id TEXT PRIMARY KEY, dataset_id TEXT NOT NULL REFERENCES saved_datasets(id) ON DELETE CASCADE,
 ordinal INTEGER NOT NULL, metadata TEXT NOT NULL, provenance TEXT NOT NULL,
 mapping TEXT NOT NULL, initial_mapping TEXT NOT NULL, mapping_source TEXT NOT NULL,
 has_uncertainty INTEGER NOT NULL);
CREATE TABLE IF NOT EXISTS saved_samples (
 curve_id TEXT NOT NULL REFERENCES saved_curves(id) ON DELETE CASCADE, ordinal INTEGER NOT NULL,
 position REAL NOT NULL, dose REAL NOT NULL, uncertainty REAL NULL, PRIMARY KEY(curve_id,ordinal));
CREATE TABLE IF NOT EXISTS saved_originals (
 dataset_id TEXT NOT NULL REFERENCES saved_datasets(id) ON DELETE CASCADE, ordinal INTEGER NOT NULL,
 name TEXT NOT NULL, content BLOB NOT NULL, PRIMARY KEY(dataset_id,ordinal));
"""

/// Creates additive dataset tables without changing existing simulation tables.
let initialize settings =
    try
        use connection = connect settings
        execute connection None schema [] |> ignore
        Ok ()
    with ex -> Error $"Cannot initialize datasets: {ex.Message}"

/// Reads all samples for one curve in acquisition order.
let private points connection id =
    use cmd = command connection None "SELECT position,dose,uncertainty FROM saved_samples WHERE curve_id=$id ORDER BY ordinal" ["$id",box id]
    use reader = cmd.ExecuteReader()
    [while reader.Read() do yield {Position=reader.GetDouble(0);Dose=reader.GetDouble(1);Uncertainty=(if reader.IsDBNull(2) then 0. else reader.GetDouble(2))}]

/// Reads saved metadata separately from samples to avoid nested active readers.
let private curves connection datasetId withPoints =
    let records =
        use cmd = command connection None "SELECT id,metadata,provenance,mapping,mapping_source,has_uncertainty FROM saved_curves WHERE dataset_id=$id ORDER BY ordinal" ["$id",box datasetId]
        use reader = cmd.ExecuteReader()
        [while reader.Read() do
            yield {Id=reader.GetString(0);Metadata=JsonSerializer.Deserialize<CurveMetadata>(reader.GetString(1))
                   Provenance=JsonSerializer.Deserialize<CurveProvenance>(reader.GetString(2));Mapping=JsonSerializer.Deserialize<AnalysisMapping>(reader.GetString(3))
                   MappingSource=reader.GetString(4);HasUncertainty=reader.GetBoolean(5);Points=[]}]
    records |> List.map (fun curve -> if withPoints then {curve with Points=points connection curve.Id} else curve)

/// Reads dataset headers without loading numeric samples or original blobs.
let private summaries connection =
    let records =
        use cmd = command connection None "SELECT id,name,source,acquired_at,saved_at,collection_key,revision FROM saved_datasets ORDER BY saved_at DESC,id" []
        use reader = cmd.ExecuteReader()
        [while reader.Read() do
            yield {Id=reader.GetString(0);Name=reader.GetString(1);Source=reader.GetString(2);AcquisitionDate=reader.GetString(3)
                   SavedAt=reader.GetString(4);CollectionKey=reader.GetString(5);Revision=reader.GetInt32(6);Curves=[]}]
    records |> List.map (fun summary ->
        {summary with Curves=curves connection summary.Id false |> List.map (fun c -> {Id=summary.Id+"/"+c.Id;Label=c.Metadata.Name})})

/// Lists saved dataset versions independently of the original output files.
let list settings =
    try
        result {
            do! initialize settings
            use connection = connect settings
            return summaries connection
        }
    with ex -> Error $"Cannot list saved datasets: {ex.Message}"

/// Loads a complete dataset for metadata review.
let load settings id =
    try
        result {
            do! initialize settings
            use connection = connect settings
            let! summary = summaries connection |> List.tryFind (fun item -> item.Id=id) |> Result.requireSome "Saved dataset no longer exists."
            return {Summary=summary;Curves=curves connection id true}
        }
    with ex -> Error $"Cannot load saved dataset: {ex.Message}"

/// Saves metadata, samples and original bytes in one atomic transaction.
let create settings name source acquired collectionKey (originals: (ImportFile * byte[]) list) (items: DatasetCurve list) =
    try
        result {
            do! initialize settings
            use connection = connect settings
            use transaction = connection.BeginTransaction()
            let id = Guid.NewGuid().ToString("N")
            let savedAt = DateTimeOffset.UtcNow.ToString("O")
            execute connection (Some transaction) "INSERT INTO saved_datasets(id,name,source,acquired_at,saved_at,collection_key) VALUES($id,$name,$source,$date,$saved,$key)"
                ["$id",box id;"$name",box name;"$source",box source;"$date",box acquired;"$saved",box savedAt;"$key",box collectionKey] |> ignore
            let saved = items |> List.map (fun curve -> {curve with Id=Guid.NewGuid().ToString("N")})
            for ordinal,curve in List.indexed saved do
                let mapping = JsonSerializer.Serialize curve.Mapping
                execute connection (Some transaction) "INSERT INTO saved_curves VALUES($id,$dataset,$ordinal,$metadata,$provenance,$mapping,$mapping,$source,$uncertainty)"
                    ["$id",box curve.Id;"$dataset",box id;"$ordinal",box ordinal;"$metadata",box (JsonSerializer.Serialize curve.Metadata)
                     "$provenance",box (JsonSerializer.Serialize curve.Provenance);"$mapping",box mapping;"$source",box curve.MappingSource;"$uncertainty",box curve.HasUncertainty] |> ignore
                use insert = command connection (Some transaction) "INSERT INTO saved_samples VALUES($id,$ordinal,$position,$dose,$uncertainty)" []
                insert.Parameters.AddWithValue("$id",curve.Id) |> ignore
                insert.Parameters.Add("$ordinal",SqliteType.Integer) |> ignore
                insert.Parameters.Add("$position",SqliteType.Real) |> ignore
                insert.Parameters.Add("$dose",SqliteType.Real) |> ignore
                insert.Parameters.Add("$uncertainty",SqliteType.Real) |> ignore
                insert.Prepare()
                for index,point in List.indexed curve.Points do
                    insert.Parameters["$ordinal"].Value <- box index
                    insert.Parameters["$position"].Value <- box point.Position
                    insert.Parameters["$dose"].Value <- box point.Dose
                    insert.Parameters["$uncertainty"].Value <- if curve.HasUncertainty then box point.Uncertainty else box DBNull.Value
                    insert.ExecuteNonQuery() |> ignore
            for index,(file,bytes) in List.indexed originals do
                execute connection (Some transaction) "INSERT INTO saved_originals VALUES($dataset,$ordinal,$name,$content)"
                    ["$dataset",box id;"$ordinal",box index;"$name",box file.Name;"$content",box bytes] |> ignore
            transaction.Commit()
            return {Id=id;Name=name;Source=source;AcquisitionDate=acquired;SavedAt=savedAt;CollectionKey=collectionKey;Revision=1
                    Curves=saved |> List.map (fun c -> {Id=id+"/"+c.Id;Label=c.Metadata.Name})}
        }
    with ex -> Error $"Cannot save dataset: {ex.Message}"

/// Checks dataset identity and optimistic revision inside a write transaction.
let private updateHeader connection transaction (request: UpdateDatasetRequest) =
    execute connection (Some transaction) "UPDATE saved_datasets SET name=$name,source=$source,acquired_at=$date,revision=revision+1 WHERE id=$id AND revision=$revision"
        ["$name",box request.Name;"$source",box request.Source;"$date",box request.AcquisitionDate;"$id",box request.Id;"$revision",box request.Revision]
    |> (=) 1 |> Result.requireTrue "Dataset changed or was deleted. Reload before saving."

/// Updates reviewed metadata while preserving original samples and parser provenance.
let update settings (request: UpdateDatasetRequest) =
    try
        result {
            let! existing = load settings request.Id
            let before = existing.Curves |> List.map (fun c -> c.Id,c) |> Map.ofList
            let headerCorrections =
                ["Dataset name",existing.Summary.Name,request.Name;"Source",existing.Summary.Source,request.Source
                 "Dataset acquisition date",existing.Summary.AcquisitionDate,request.AcquisitionDate]
                |> List.choose (fun (label,a,b) -> if a=b then None else Some ($"{label}: '{a}' → '{b}'"))
            let reviewedIds = request.Curves |> List.map fst |> List.sort
            let savedIds = before |> Map.keys |> Seq.sort |> Seq.toList
            do! Result.requireTrue "Metadata must identify every saved curve exactly once." (reviewedIds=savedIds)
            do! request.Curves |> List.traverseResultM (fun (id,metadata) -> Datasets.validateMetadata (before[id].Metadata.PositionUnit="index") metadata) |> Result.map ignore
            use connection = connect settings
            use transaction = connection.BeginTransaction()
            do! updateHeader connection transaction request
            for id,metadata in request.Curves do
                let original = before[id]
                do! Result.requireTrue "Indexed simulation coordinates must remain indexed." ((original.Metadata.PositionUnit="index")=(metadata.PositionUnit="index"))
                let mapping =
                    if metadata.PositionUnit<>original.Metadata.PositionUnit then Datasets.mapping metadata
                    else {original.Mapping with Role=metadata.Role;Axis=(Datasets.mapping metadata).Axis}
                let provenance = {original.Provenance with Corrections=original.Provenance.Corrections @ headerCorrections @ Datasets.corrections original.Metadata metadata}
                execute connection (Some transaction) "UPDATE saved_curves SET metadata=$metadata,provenance=$provenance,mapping=$mapping WHERE id=$id AND dataset_id=$dataset"
                    ["$metadata",box (JsonSerializer.Serialize metadata);"$provenance",box (JsonSerializer.Serialize provenance);"$mapping",box (JsonSerializer.Serialize mapping)
                     "$id",box id;"$dataset",box request.Id] |> ignore
            transaction.Commit()
            return ()
        }
    with ex -> Error $"Cannot update dataset: {ex.Message}"

/// Deletes only the selected saved version and its own archived data.
let delete settings (request: DeleteDatasetRequest) =
    try
        use connection = connect settings
        execute connection None "DELETE FROM saved_datasets WHERE id=$id AND revision=$revision" ["$id",box request.Id;"$revision",box request.Revision]
        |> (=) 1 |> Result.requireTrue "Dataset changed or was deleted. Reload before deleting."
    with ex -> Error $"Cannot delete dataset: {ex.Message}"

/// Returns an archived original byte for byte without accessing caller-supplied paths.
let original settings (request: DatasetFileRequest) =
    try
        use connection = connect settings
        use cmd = command connection None "SELECT name,content FROM saved_originals WHERE dataset_id=$id AND ordinal=$ordinal" ["$id",box request.DatasetId;"$ordinal",box request.FileIndex]
        use reader = cmd.ExecuteReader()
        if reader.Read() then Ok {Name=reader.GetString(0);ContentBase64=Convert.ToBase64String(reader.GetFieldValue<byte[]>(1))}
        else Error "Original file no longer exists."
    with ex -> Error $"Cannot read original file: {ex.Message}"

/// Resolves a saved curve by dataset and curve identities.
let loadAnalysisCurve settings (id: string) =
    try
        result {
            let parts = id.Split('/')
            do! Result.requireTrue "Invalid saved curve identifier." (parts.Length=2)
            use connection = connect settings
            let! dataset = summaries connection |> List.tryFind (fun d -> d.Id=parts[0]) |> Result.requireSome "Saved dataset no longer exists."
            let! curve = curves connection parts[0] false |> List.tryFind (fun c -> c.Id=parts[1]) |> Result.requireSome "Saved curve no longer exists."
            return {Dataset={Id=id;Label=dataset.Name+" · "+curve.Metadata.Name};Mapping=Some curve.Mapping;MappingSource=curve.MappingSource
                    Points=points connection curve.Id;ValueUnit=curve.Metadata.ValueUnit;HasUncertainty=curve.HasUncertainty}
        }
    with ex -> Error $"Cannot load saved curve: {ex.Message}"

/// Saves a physical override without mutating original numeric samples.
let saveMapping settings (request: SaveAnalysisMapping) =
    try
        result {
            let! _ = Analysis.validateMapping request.Mapping
            let! existing = loadAnalysisCurve settings request.Id
            let parts = request.Id.Split('/')
            use connection = connect settings
            let stored = curves connection parts[0] false |> List.find (fun c -> c.Id=parts[1])
            use transaction = connection.BeginTransaction()
            execute connection (Some transaction) "UPDATE saved_curves SET mapping=$mapping,mapping_source='Saved manual override.' WHERE id=$id AND dataset_id=$dataset"
                ["$mapping",box (JsonSerializer.Serialize request.Mapping);"$id",box parts[1];"$dataset",box parts[0]] |> ignore
            let previous = existing.Mapping |> Option.map (fun mapping -> JsonSerializer.Serialize mapping) |> Option.defaultValue "unknown"
            let correction = DateTimeOffset.UtcNow.ToString("O")+": mapping "+previous+" → "+JsonSerializer.Serialize request.Mapping
            let provenance = {stored.Provenance with Corrections=stored.Provenance.Corrections @ [correction]}
            execute connection (Some transaction) "UPDATE saved_curves SET provenance=$provenance WHERE id=$id" ["$provenance",box (JsonSerializer.Serialize provenance);"$id",box parts[1]] |> ignore
            execute connection (Some transaction) "UPDATE saved_datasets SET revision=revision+1 WHERE id=$id" ["$id",box parts[0]] |> ignore
            transaction.Commit()
            return ()
        }
    with ex -> Error $"Cannot save mapping: {ex.Message}"

/// Restores the geometry snapshot recorded when a dataset was imported.
let resetMapping settings (id: string) =
    try
        result {
            let! _ = loadAnalysisCurve settings id
            let parts = id.Split('/')
            let mapping =
                use connection = connect settings
                use cmd = command connection None "SELECT initial_mapping FROM saved_curves WHERE id=$id AND dataset_id=$dataset" ["$id",box parts[1];"$dataset",box parts[0]]
                JsonSerializer.Deserialize<AnalysisMapping>(string (cmd.ExecuteScalar()))
            do! saveMapping settings {Id=id;Mapping=mapping}
            return! loadAnalysisCurve settings id
        }
    with ex -> Error $"Cannot restore imported geometry: {ex.Message}"
