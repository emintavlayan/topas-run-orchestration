module SceneOperation

open System
open System.IO
open FsToolkit.ErrorHandling
open Shared
open TsebtConfig

/// Resolves a path within a configured root and rejects symlink traversal.
let confinedPath root relative =
    try
        let root = Path.GetFullPath root
        let target = Path.GetFullPath(Path.Combine(root, relative))

        let prefix =
            root.TrimEnd(Path.DirectorySeparatorChar) + string Path.DirectorySeparatorChar

        if not (target.StartsWith(prefix, StringComparison.Ordinal)) then
            Error "Path is outside the configured directory."
        else
            let mutable current = root
            let mutable linked = false

            for part in Path.GetRelativePath(root, target).Split(Path.DirectorySeparatorChar) do
                current <- Path.Combine(current, part)

                if File.Exists current || Directory.Exists current then
                    if File.GetAttributes(current).HasFlag FileAttributes.ReparsePoint then
                        linked <- true

            if linked then
                Error "Linked geometry paths are not supported."
            else
                Ok target
    with ex ->
        Error ex.Message

/// Loads only an existing generated input with a bounded file size.
let private readInput (settings: TsebtSettings) id = result {
    let! path = confinedPath (Path.Combine(settings.AppRoot, settings.Paths.Inputs)) id

    if Path.GetExtension(path) <> ".txt" || not (File.Exists path) then
        return! Error "Generated input not found."

    if FileInfo(path).Length > 8L * 1024L * 1024L then
        return! Error "Generated input exceeds the 8 MiB viewer limit."

    return path, File.ReadAllText path
}

/// Selects the first readable geometry input from each run in deterministic order.
let listInputs (settings: TsebtSettings) =
    try
        let root = Path.Combine(settings.AppRoot, settings.Paths.Inputs)

        if not (Directory.Exists root) then
            Ok []
        else
            Directory.EnumerateDirectories root
            |> Seq.filter (fun folder -> not (File.GetAttributes(folder).HasFlag FileAttributes.ReparsePoint))
            |> Seq.sortByDescending (fun folder -> DirectoryInfo(folder).Name)
            |> Seq.choose (fun folder ->
                Directory.EnumerateFiles(folder, "*.txt", SearchOption.TopDirectoryOnly)
                |> Seq.sortBy Path.GetFileName
                |> Seq.tryPick (fun file ->
                    let id = Path.GetRelativePath(root, file).Replace('\\', '/')

                    match readInput settings id |> Result.bind (snd >> TopasScene.parse) with
                    | Ok _ ->
                        Some {
                            Id = id
                            Batch = DirectoryInfo(folder).Name
                            FileName = Path.GetFileName file
                        }
                    | Error _ -> None))
            |> Seq.toList
            |> Ok
    with ex ->
        Error $"Cannot list generated inputs: {ex.Message}"

/// Resolves source headers using the simulation working directory, including external source libraries.
let private sourceBounds workingDirectory (source: SceneSource) = result {
    let! file = source.FilePath |> Result.requireSome "PhaseSpaceFileName is missing."

    let file =
        if Path.IsPathRooted file then
            file
        else
            Path.Combine(workingDirectory, file)

    let stem =
        if
            [ ".phsp"; ".IAEAphsp"; ".header"; ".IAEAheader" ]
            |> List.exists (fun suffix -> file.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
        then
            Path.Combine(Path.GetDirectoryName file, Path.GetFileNameWithoutExtension file)
        else
            file

    let! path =
        [ stem + ".header"; stem + ".IAEAheader" ]
        |> List.tryFind File.Exists
        |> Result.requireSome "Phase-space header not found."

    use stream = File.OpenRead path

    if stream.Length > 1024L * 1024L then
        return! Error "Phase-space header exceeds 1 MiB."

    use reader = new StreamReader(stream)
    let text = reader.ReadToEnd()
    return! PhaseSpaceGeometry.parseBounds source.PositionScale text
}

/// Adds measured source bounds while retaining the scene when a source header is unavailable.
let private enrichSources workingDirectory (scene: SimulationScene) =
    let sources, warnings =
        scene.Sources
        |> List.map (fun source ->
            if not (String.Equals(source.SourceType, "PhaseSpace", StringComparison.OrdinalIgnoreCase)) then
                source, None
            else
                let bounds =
                    try
                        sourceBounds workingDirectory source
                    with ex ->
                        Error ex.Message

                match bounds with
                | Ok bounds -> { source with Bounds = Some bounds }, None
                | Error reason -> source, Some $"{source.Name}: phase-space footprint unavailable. {reason}")
        |> List.unzip

    {
        scene with
            Sources = sources
            Warnings = scene.Warnings @ List.choose id warnings
    }

/// Parses a selected generated input and resolves source headers without mutating simulation data.
let load settings id =
    try
        result {
            let! path, text = readInput settings id
            let! scene = TopasScene.parse text
            let batch = DirectoryInfo(Path.GetDirectoryName path).Name
            return enrichSources (Path.Combine(settings.AppRoot, settings.Paths.Runs, batch)) scene
        }
    with ex ->
        Error $"Cannot load generated input: {ex.Message}"

/// Reads an STL named by a parsed CAD component inside the simulation workspace.
let loadAsset (settings: TsebtSettings) (request: SceneAssetRequest) =
    try
        result {
            let! inputPath, text = readInput settings request.InputId
            let! scene = TopasScene.parse text

            let! owner =
                scene.Components
                |> List.tryFind (fun c -> c.Name = request.Component)
                |> Result.requireSome "CAD component not found."

            let! assetPath =
                match owner.Shape with
                | Cad(path, _) -> Ok path
                | _ -> Error "Component is not a CAD asset."

            let batch = DirectoryInfo(Path.GetDirectoryName inputPath).Name
            let workingDirectory = Path.Combine(settings.AppRoot, settings.Paths.Runs, batch)

            let candidate =
                if Path.IsPathRooted assetPath then
                    assetPath
                else
                    Path.Combine(workingDirectory, assetPath)

            let! path = confinedPath settings.AppRoot (Path.GetRelativePath(settings.AppRoot, candidate))

            if not (String.Equals(Path.GetExtension(path), ".stl", StringComparison.OrdinalIgnoreCase)) then
                return! Error "Only STL CAD assets are supported."

            if not (File.Exists path) then
                return! Error $"CAD asset not found: {assetPath}"

            if FileInfo(path).Length > 32L * 1024L * 1024L then
                return! Error "STL exceeds the 32 MiB viewer limit."

            return {
                Name = Path.GetFileName path
                Base64 = Convert.ToBase64String(File.ReadAllBytes path)
            }
        }
    with ex ->
        Error $"Cannot load CAD asset: {ex.Message}"