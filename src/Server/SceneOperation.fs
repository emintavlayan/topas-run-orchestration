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

/// Lists generated inputs without reading templates or starting simulations.
let listInputs (settings: TsebtSettings) =
    try
        let root = Path.Combine(settings.AppRoot, settings.Paths.Inputs)

        if not (Directory.Exists root) then
            Ok []
        else
            Directory.EnumerateDirectories root
            |> Seq.filter (fun folder -> not (File.GetAttributes(folder).HasFlag FileAttributes.ReparsePoint))
            |> Seq.collect (fun folder -> Directory.EnumerateFiles(folder, "*.txt", SearchOption.TopDirectoryOnly))
            |> Seq.filter (fun file -> not (File.GetAttributes(file).HasFlag FileAttributes.ReparsePoint))
            |> Seq.map (fun file -> {
                Id = Path.GetRelativePath(root, file).Replace('\\', '/')
                Batch = DirectoryInfo(Path.GetDirectoryName file).Name
                FileName = Path.GetFileName file
            })
            |> Seq.sortByDescending (fun input -> input.Batch, input.FileName)
            |> Seq.toList
            |> Ok
    with ex ->
        Error $"Cannot list generated inputs: {ex.Message}"

/// Loads only an existing generated input with a bounded file size.
let private readInput (settings: TsebtSettings) id = result {
    let! path = confinedPath (Path.Combine(settings.AppRoot, settings.Paths.Inputs)) id

    if Path.GetExtension(path) <> ".txt" || not (File.Exists path) then
        return! Error "Generated input not found."

    if FileInfo(path).Length > 8L * 1024L * 1024L then
        return! Error "Generated input exceeds the 8 MiB viewer limit."

    return path, File.ReadAllText path
}

/// Parses a selected generated input without mutating it.
let load settings id =
    try
        readInput settings id |> Result.bind (snd >> TopasScene.parse)
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