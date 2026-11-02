module SqliteInit

open System
open Microsoft.Data.Sqlite
open TsebtConfig
open Bootstrap

/// Resolves the SQLite database file path from settings.
let getDatabasePath (settings: TsebtSettings) : string =
    combineAppRoot settings.AppRoot settings.Paths.Database

/// Executes a single SQL statement against an open connection.
let private executeNonQuery (connection: SqliteConnection) (sql: string) : unit =
    use command = connection.CreateCommand()
    command.CommandText <- sql
    command.ExecuteNonQuery() |> ignore

/// Returns true when the given table has the given column name.
let private hasColumn (connection: SqliteConnection) (tableName: string) (columnName: string) : bool =
    use command = connection.CreateCommand()
    command.CommandText <- $"PRAGMA table_info({tableName});"
    use reader = command.ExecuteReader()
    let mutable found = false

    while reader.Read() && not found do
        let currentName = reader.GetString(1)

        if String.Equals(currentName, columnName, StringComparison.OrdinalIgnoreCase) then
            found <- true

    found

/// Adds a column to generated_batches only when it does not already exist.
let private ensureGeneratedBatchColumn
    (connection: SqliteConnection)
    (columnName: string)
    (columnSqlTypeAndDefault: string)
    : unit =
    if not (hasColumn connection "generated_batches" columnName) then
        executeNonQuery connection $"ALTER TABLE generated_batches ADD COLUMN {columnName} {columnSqlTypeAndDefault};"

/// Returns true when the given table exists in the current database.
let private tableExists (connection: SqliteConnection) (tableName: string) : bool =
    use command = connection.CreateCommand()
    command.CommandText <- "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = $tableName;"
    command.Parameters.AddWithValue("$tableName", tableName) |> ignore
    Convert.ToInt32(command.ExecuteScalar()) > 0

/// Ensures run foundation columns exist in generated_batches for backward-compatible databases.
let private ensureRunFoundationColumns (connection: SqliteConnection) : unit =
    ensureGeneratedBatchColumn connection "run_status" "TEXT NOT NULL DEFAULT 'Generated'"
    ensureGeneratedBatchColumn connection "slurm_job_id" "TEXT NULL"
    ensureGeneratedBatchColumn connection "manifest_path" "TEXT NULL"
    ensureGeneratedBatchColumn connection "script_path" "TEXT NULL"
    ensureGeneratedBatchColumn connection "submitted_at" "TEXT NULL"

/// Ensures collect foundation columns exist in generated_batches for backward-compatible databases.
let private ensureCollectFoundationColumns (connection: SqliteConnection) : unit =
    ensureGeneratedBatchColumn connection "collect_status" "TEXT NOT NULL DEFAULT 'NotCollected'"
    ensureGeneratedBatchColumn connection "collected_at" "TEXT NULL"
    ensureGeneratedBatchColumn connection "collect_output_folder" "TEXT NULL"
    ensureGeneratedBatchColumn connection "collect_summary_path" "TEXT NULL"
    ensureGeneratedBatchColumn connection "collect_csv_found_count" "INTEGER NULL"
    ensureGeneratedBatchColumn connection "collect_csv_missing_count" "INTEGER NULL"
    ensureGeneratedBatchColumn connection "collect_log_found_count" "INTEGER NULL"
    ensureGeneratedBatchColumn connection "collect_log_missing_count" "INTEGER NULL"

/// Ensures the generated_batch_collections history table exists for recollection tracking.
let private ensureGeneratedBatchCollectionsTable (connection: SqliteConnection) : unit =
    executeNonQuery
        connection
        """CREATE TABLE IF NOT EXISTS generated_batch_collections (
  id INTEGER PRIMARY KEY AUTOINCREMENT,
  seed_base TEXT NOT NULL,
  status TEXT NOT NULL,
  started_at TEXT NOT NULL,
  completed_at TEXT NULL,
  output_folder TEXT NULL,
  summary_path TEXT NULL,
  csv_found_count INTEGER NULL,
  csv_missing_count INTEGER NULL,
  log_found_count INTEGER NULL,
  log_missing_count INTEGER NULL,
  error_message TEXT NULL
);"""

    executeNonQuery
        connection
        """CREATE INDEX IF NOT EXISTS idx_generated_batch_collections_seed_base_started_at
ON generated_batch_collections(seed_base, started_at DESC, id DESC);"""

/// Backfills collection history rows from the latest generated_batches metadata when possible.
let private backfillExistingCollectionHistory (connection: SqliteConnection) : unit =
    if tableExists connection "generated_batch_collections" then
        executeNonQuery
            connection
            """INSERT INTO generated_batch_collections (
  seed_base,
  status,
  started_at,
  completed_at,
  output_folder,
  summary_path,
  csv_found_count,
  csv_missing_count,
  log_found_count,
  log_missing_count,
  error_message
)
SELECT
  b.seed_base,
  COALESCE(NULLIF(b.collect_status, ''), 'Collected'),
  COALESCE(b.collected_at, b.created_at),
  b.collected_at,
  b.collect_output_folder,
  b.collect_summary_path,
  b.collect_csv_found_count,
  b.collect_csv_missing_count,
  b.collect_log_found_count,
  b.collect_log_missing_count,
  NULL
FROM generated_batches b
WHERE b.collect_output_folder IS NOT NULL
  AND NOT EXISTS (
    SELECT 1
    FROM generated_batch_collections c
    WHERE c.seed_base = b.seed_base
      AND c.output_folder = b.collect_output_folder
  );"""

/// Initializes SQLite file and required tables if missing.
let initialize (settings: TsebtSettings) : Result<string, string> =
    let databasePath = getDatabasePath settings

    try
        let connectionStringBuilder = SqliteConnectionStringBuilder()
        connectionStringBuilder.DataSource <- databasePath

        use connection = new SqliteConnection(connectionStringBuilder.ConnectionString)
        connection.Open()

        executeNonQuery
            connection
            """CREATE TABLE IF NOT EXISTS generated_batches (
  id INTEGER PRIMARY KEY AUTOINCREMENT,
  seed_base TEXT NOT NULL,
  created_at TEXT NOT NULL,
  run_status TEXT NOT NULL DEFAULT 'Generated',
  slurm_job_id TEXT NULL,
  manifest_path TEXT NULL,
  script_path TEXT NULL,
  submitted_at TEXT NULL,
  collect_status TEXT NOT NULL DEFAULT 'NotCollected',
  collected_at TEXT NULL,
  collect_output_folder TEXT NULL,
  collect_summary_path TEXT NULL,
  collect_csv_found_count INTEGER NULL,
  collect_csv_missing_count INTEGER NULL,
  collect_log_found_count INTEGER NULL,
  collect_log_missing_count INTEGER NULL
);"""

        executeNonQuery
            connection
            """CREATE TABLE IF NOT EXISTS generated_runs (
  id INTEGER PRIMARY KEY AUTOINCREMENT,
  batch_id INTEGER NOT NULL,
  run_id TEXT NOT NULL,
  phase_space_index TEXT NOT NULL,
  phase_space_file TEXT NOT NULL,
  node_name TEXT NOT NULL,
  node_digit TEXT NOT NULL,
  seed TEXT NOT NULL,
  input_file_path TEXT NOT NULL,
  output_file_path TEXT NOT NULL,
  run_folder TEXT NOT NULL,
  status TEXT NOT NULL,
  created_at TEXT NOT NULL,
  FOREIGN KEY (batch_id) REFERENCES generated_batches(id)
);"""

        ensureRunFoundationColumns connection
        executeNonQuery connection """CREATE TABLE IF NOT EXISTS run_node_submissions (
  id INTEGER PRIMARY KEY AUTOINCREMENT,
  seed_base TEXT NOT NULL,
  node_name TEXT NOT NULL,
  job_id TEXT NOT NULL UNIQUE,
  manifest_path TEXT NOT NULL,
  submitted_at TEXT NOT NULL
);"""
        executeNonQuery connection "CREATE INDEX IF NOT EXISTS idx_run_node_submissions_batch ON run_node_submissions(seed_base);"
        ensureCollectFoundationColumns connection
        ensureGeneratedBatchCollectionsTable connection
        backfillExistingCollectionHistory connection

        match DatasetStore.initialize settings with
        | Ok () -> Ok databasePath
        | Error message -> Error message
    with ex ->
        Error $"Failed to initialize SQLite: {ex.Message}"
