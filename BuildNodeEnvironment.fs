module BuildNodeEnvironment

open System
open System.Diagnostics
open System.IO
open System.Text.Json
open System.Text.RegularExpressions
open FsToolkit.ErrorHandling

/// Represents the repository's Node and npm requirements.
type Requirements = { NodeVersion: string; NpmMajor: int }

/// Represents a process that can be executed without shell interpolation.
type Command = { Executable: string; Arguments: string list }

/// Represents one verified Node installation and its matching npm tools.
type Toolchain = { Node: string; Npm: string; Npx: string; NodeVersion: string; NpmVersion: string }

/// Reads the version pin and supported npm major from repository configuration.
let readRequirements repositoryRoot =
    try
        let version=File.ReadAllText(Path.Combine(repositoryRoot,".node-version")).Trim().TrimStart('v')
        use package=JsonDocument.Parse(File.ReadAllText(Path.Combine(repositoryRoot,"src","Client","package.json")))
        let npm=package.RootElement.GetProperty("engines").GetProperty("npm").GetString()
        if not(Regex.IsMatch(version,@"^\d+\.\d+\.\d+$")) then Error ".node-version must pin a complete Node version, for example 20.20.2."
        elif not(Regex.IsMatch(npm,@"^\d+\.x$")) then Error "The build requires engines.npm to specify one major version, for example 10.x."
        else Ok {NodeVersion=version;NpmMajor=Int32.Parse(npm.Split('.')[0])}
    with ex -> Error $"Cannot read Node build requirements: {ex.Message}"

/// Runs a build tool directly and captures its output with an installation-friendly timeout.
let run (command:Command) =
    try
        let start=ProcessStartInfo(command.Executable,UseShellExecute=false,RedirectStandardOutput=true,RedirectStandardError=true)
        for argument in command.Arguments do start.ArgumentList.Add argument
        use child=new Process(StartInfo=start)
        child.Start() |> ignore
        let output=child.StandardOutput.ReadToEndAsync()
        let errors=child.StandardError.ReadToEndAsync()
        if not(child.WaitForExit(300000)) then
            try child.Kill(true) with _ -> ()
            Error $"{command.Executable} timed out after five minutes."
        else
            let stdout=output.GetAwaiter().GetResult().Trim()
            let stderr=errors.GetAwaiter().GetResult().Trim()
            if child.ExitCode=0 then Ok stdout
            else Error $"{Path.GetFileName command.Executable} exited with {child.ExitCode}: {stderr} {stdout}"
    with ex -> Error $"Cannot run {command.Executable}: {ex.Message}"

/// Finds an executable on the current process PATH without invoking a shell.
let findOnPath isWindows (name:string) =
    let file=if isWindows then name+".exe" else name
    (Environment.GetEnvironmentVariable "PATH" |> Option.ofObj |> Option.defaultValue "").Split(Path.PathSeparator)
    |> Array.filter(String.IsNullOrWhiteSpace >> not)
    |> Array.map(fun folder -> Path.Combine(folder.Trim('"'),file))
    |> Array.tryFind File.Exists

/// Lists known version-manager locations without depending on shell profiles or refreshed PATH.
let managerLocations isWindows environment =
    let home=environment "USERPROFILE" |> Option.orElseWith(fun () -> environment "HOME")
    let roots=
        if isWindows then
            [environment "LOCALAPPDATA" |> Option.map(fun root -> Path.Combine(root,"Microsoft","WinGet"))
             environment "ProgramFiles" |> Option.map(fun root -> Path.Combine(root,"WinGet"))] |> List.choose id
        else []
    let links=roots |> List.map(fun root -> Path.Combine(root,"Links","fnm.exe"))
    let packages=roots |> List.collect(fun root ->
        try
            let folder=Path.Combine(root,"Packages")
            if not(Directory.Exists folder) then []
            else Directory.GetDirectories(folder,"Schniz.fnm_*") |> Array.collect(fun package -> Directory.GetFiles(package,"fnm.exe",SearchOption.AllDirectories)) |> Array.toList
        with _ -> [])
    let user=
        home |> Option.map(fun root ->
            if isWindows then [Path.Combine(root,"scoop","apps","fnm","current","fnm.exe");Path.Combine(root,".cargo","bin","fnm.exe")]
            else [Path.Combine(root,".local","share","fnm","fnm");Path.Combine(root,".fnm","fnm");Path.Combine(root,".cargo","bin","fnm")]) |> Option.defaultValue []
    let extra=if isWindows then [] else ["/opt/homebrew/bin/fnm";"/usr/local/bin/fnm"]
    links @ packages @ user @ extra

/// Finds fnm on PATH or in known portable installation directories, including WinGet.
let findManager isWindows environment lookup =
    lookup "fnm" |> Option.orElseWith(fun () -> managerLocations isWindows environment |> List.tryFind File.Exists)

/// Resolves npm's JavaScript entry points beside the selected Node installation.
let toolPaths isWindows (node:string) = result {
    let folder=Path.GetDirectoryName node
    let candidates=
        [Path.Combine(folder,"node_modules","npm","bin")
         Path.GetFullPath(Path.Combine(folder,"..","lib","node_modules","npm","bin"))]
    let directory=candidates |> List.tryFind(fun path -> File.Exists(Path.Combine(path,"npm-cli.js")) && File.Exists(Path.Combine(path,"npx-cli.js")))
    match directory with
    | Some path -> return Path.Combine(path,"npm-cli.js"),Path.Combine(path,"npx-cli.js")
    | None when not isWindows && File.Exists(Path.Combine(folder,"npm")) && File.Exists(Path.Combine(folder,"npx")) ->
        return Path.Combine(folder,"npm"),Path.Combine(folder,"npx")
    | None -> return! Error $"npm/npx are missing beside {node}. Install the complete Node distribution with fnm."
}

/// Verifies both runtime versions using npm from the selected installation, never an unrelated PATH entry.
let inspect (runner:Command -> Result<string,string>) isWindows (requirements:Requirements) node = result {
    do! Result.requireTrue $"Node executable does not exist: {node}" (File.Exists node)
    let! version=runner {Executable=node;Arguments=["--version"]}
    do! Result.requireTrue $"Expected Node {requirements.NodeVersion}; found {version} at {node}." (version.Trim().TrimStart('v')=requirements.NodeVersion)
    let! npm,npx=toolPaths isWindows node
    let! npmVersion=runner {Executable=node;Arguments=[npm;"--version"]}
    do! Result.requireTrue $"Expected npm {requirements.NpmMajor}.x beside {node}; found {npmVersion}." (Regex.IsMatch(npmVersion,$"^{requirements.NpmMajor}\\.\\d+\\.\\d+$"))
    return {Node=node;Npm=npm;Npx=npx;NodeVersion=version;NpmVersion=npmVersion}
}

/// Selects an installed matching toolchain or installs the pinned version through profile-independent fnm exec.
let select (runner:Command -> Result<string,string>) isWindows (requirements:Requirements) currentNode manager =
    let current=currentNode |> Option.map(inspect runner isWindows requirements) |> Option.defaultValue(Error "Node is not on PATH.")
    match current with
    | Ok toolchain -> Ok toolchain
    | Error currentError -> result {
        let! fnm=manager |> Result.requireSome (currentError+" Install fnm once (Windows: winget install --id Schniz.fnm -e), then retry the same build command. No shell profile setup is required.")
        let command={Executable=fnm;Arguments=["exec";"--using";requirements.NodeVersion;"--";"node";"-p";"process.execPath"]}
        let resolved=
            match runner command with
            | Ok path -> Ok path
            | Error _ -> result {
                printfn "Installing project Node %s through fnm..." requirements.NodeVersion
                let! _=runner {Executable=fnm;Arguments=["install";requirements.NodeVersion]}
                return! runner command
            }
        let! path=resolved |> Result.mapError(fun error -> $"Cannot prepare project Node {requirements.NodeVersion}: {error}")
        return! inspect runner isWindows requirements (path.Trim())
    }

/// Prepares Node for this build process and all descendants without changing the user's global environment.
let prepare repositoryRoot = result {
    let isWindows=OperatingSystem.IsWindows()
    let environment name=Environment.GetEnvironmentVariable name |> Option.ofObj
    let! requirements=readRequirements repositoryRoot
    let manager=findManager isWindows environment (findOnPath isWindows)
    let! tools=select run isWindows requirements (findOnPath isWindows "node") manager
    let folder=Path.GetDirectoryName tools.Node
    Environment.SetEnvironmentVariable("PATH",folder+string Path.PathSeparator+(environment "PATH" |> Option.defaultValue ""))
    printfn "Project Node %s / npm %s: %s" tools.NodeVersion tools.NpmVersion tools.Node
    return tools
}
