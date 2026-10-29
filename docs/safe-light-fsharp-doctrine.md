# SAFE Light F# Doctrine

[README](../README.md) · [All documentation](index.md)

## 1. Shape of this SAFE project

```text
Shared
  Domain types
  API contracts
  DTOs shared by Client and Server

Server
  File IO
  Template loading
  Text replacement
  Copy/generate operations
  API implementation

Client
  Elmish Model / Msg / update / view
  UI state
  Calls server API
```

No heavy architecture.
No enterprise DDD.
No repository/factory/aggregate ceremony.

---

## 2. Core rules

```text
Raw outside, typed inside.
Effects at edges.
Small named functions.
Result for fallible work.
Pure functions by default.
Return data, not prose.
Format messages at UI/log boundary.
```

---

## 3. Error style

For this project, use:

```fsharp
Result<'T, string>
```

Reason: this is an orchestration tool, mostly reading files, editing text, copying files, generating run folders, and reporting what happened.

Use typed errors only when branching really matters.

---

## 4. Function style

Every function should be small and intent-named.

```fsharp
/// Reads template text from disk
let readTemplate path = ...

/// Replaces token in template text
let replaceToken token value text = ...

/// Builds generated TOPAS input path
let buildInputPath runId root = ...
```

Avoid:

```fsharp
let processStuff x = ...
let handle y = ...
let run2 z = ...
```

---

## 5. Module style

Use modules by responsibility, not by ceremony.

Only add files when the current one becomes unclear.

---

## 6. ROP rule

Use `FsToolkit.ErrorHandling` for sequential fallible work.

```fsharp
open FsToolkit.ErrorHandling

/// Generates TOPAS input file from selected template
let generateInput deps request =
    result {
        let! templateText = deps.ReadFile request.TemplatePath
        let renderedText = Template.render request.Parameters templateText
        let outputPath = RunPath.inputFile request.RunId deps.Root
        do! deps.WriteFile outputPath renderedText
        return { RunId = request.RunId; InputPath = outputPath }
    }
```

Use `result {}` when failure should stop the operation.

Use per-item `Result` when partial success is allowed.

---

## 7. Pure vs effectful split

Keep these pure:

```text
Path construction
Template token replacement
Validation
RunId formatting
Config mapping
Summary calculation
```

Keep these effectful and isolated:

```text
File read/write
Directory creation
Copying files
Process execution
SQLite access
Server API handler
```

---

## 8. Shared domain rule

`Shared.fs` should contain types that both sides need.

Example:

```fsharp
type RunId = RunId of string

type GenerateRunRequest =
    { RunId: RunId
      TemplateName: string
      OutputFolder: string }

type GeneratedRun =
    { RunId: RunId
      InputFile: string
      OutputFolder: string }

type ITopasApi =
    { generateRun: GenerateRunRequest -> Async<Result<GeneratedRun, string>>
      getRuns: unit -> Async<GeneratedRun list> }
```

Keep shared types boring and serializable.

---

## 9. Server rule

Server owns real work.

```text
Client asks.
Server validates.
Server reads/writes/copies/generates.
Server returns Result data.
```

Do not put file-generation logic in Elmish client code.

---

## 10. Client rule

Elmish client owns state and UI only.

```text
Model = current UI state
Msg = possible UI events
update = state transition + commands
view = HTML
```

Client should not know low-level file mechanics.

---

## 11. Boundary message rule

Domain/server returns structured data:

```fsharp
type GeneratedRun =
    { RunId: RunId
      InputFile: string
      OutputFolder: string }
```

UI turns it into prose:

```fsharp
$"Generated run {runId} at {path}"
```

Not the other way around.

---

## 12. Final rule

Keep this SAFE app simple.

Shared contains shared types and API contracts.
Server performs orchestration and effects.
Client contains Elmish state, messages, commands, and view.

Use small named functions, mostly pure code, `Result<'T,string>`, and `FsToolkit.ErrorHandling`.
Return structured data from server/domain code.
Format human messages only in UI or logging.

No heavy `Workflow`/`Main` split.
No enterprise DDD ceremony.
