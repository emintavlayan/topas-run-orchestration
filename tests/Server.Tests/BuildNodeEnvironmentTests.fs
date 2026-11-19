module Server.Tests.BuildNodeEnvironmentTests

open System
open System.IO
open Xunit
open BuildNodeEnvironment
open Server.Tests.TestHelpers

/// Creates a fake complete Node installation for deterministic environment selection tests.
let private withTools action =
    let root=Path.Combine(Path.GetTempPath(),"node-env-"+Guid.NewGuid().ToString("N"))
    let folder=Path.Combine(root,"Node with spaces")
    Directory.CreateDirectory(Path.Combine(folder,"node_modules","npm","bin")) |> ignore
    let node=Path.Combine(folder,"node.exe")
    let npm=Path.Combine(folder,"node_modules","npm","bin","npm-cli.js")
    let npx=Path.Combine(folder,"node_modules","npm","bin","npx-cli.js")
    for path in [node;npm;npx] do File.WriteAllText(path,"")
    try action root node npm
    finally Directory.Delete(root,true)

/// Represents the exact repository requirements used by selection tests.
let private requirements={NodeVersion="20.20.2";NpmMajor=10}

[<Fact>]
let ``Matching Node runs its own npm without requiring fnm or shell initialization`` () =
    withTools(fun _ node npm ->
        let calls=ResizeArray<Command>()
        /// Simulates Node and its own npm, with no version manager available.
        let runner command =
            calls.Add command
            if command.Arguments=["--version"] then Ok "v20.20.2" else Ok "10.8.2"
        let tools=select runner true requirements (Some node) None |> assertOk
        Assert.Equal(node,tools.Node)
        Assert.Equal(npm,tools.Npm)
        Assert.Equal<string list>([npm;"--version"],calls[1].Arguments)
        Assert.All(calls,fun command -> Assert.Equal(node,command.Executable)))

[<Fact>]
let ``Wrong Node and npm on PATH are replaced with the installed pinned toolchain`` () =
    withTools(fun root node _ ->
        let system=Path.Combine(root,"system-node.exe")
        File.WriteAllText(system,"")
        let calls=ResizeArray<Command>()
        /// Returns Node 24 for the inherited executable and Node 20 from fnm exec.
        let runner command =
            calls.Add command
            if command.Executable=system then Ok "v24.22.1"
            elif command.Executable="fnm.exe" then Ok node
            elif command.Arguments=["--version"] then Ok "v20.20.2"
            else Ok "10.8.2"
        let tools=select runner true requirements (Some system) (Some "fnm.exe") |> assertOk
        Assert.Equal(node,tools.Node)
        Assert.Contains(calls,fun command -> command.Arguments=["exec";"--using";"20.20.2";"--";"node";"-p";"process.execPath"])
        Assert.DoesNotContain(calls,fun command -> command.Arguments |> List.contains "use")
        Assert.DoesNotContain(calls,fun command -> command.Arguments |> List.contains "install"))

[<Fact>]
let ``Missing pinned Node is installed once and then verified`` () =
    withTools(fun _ node _ ->
        let mutable installed=false
        let mutable installs=0
        /// Simulates fnm installation followed by successful profile-independent selection.
        let runner command =
            match command.Executable,command.Arguments with
            | "fnm",["install";"20.20.2"] -> installs <- installs+1;installed <- true;Ok "installed"
            | "fnm",_ -> if installed then Ok node else Error "Version not installed"
            | _,["--version"] -> Ok "v20.20.2"
            | _ -> Ok "10.8.2"
        Assert.Equal(node,(select runner true requirements None (Some "fnm") |> assertOk).Node)
        Assert.Equal(1,installs))

[<Fact>]
let ``WinGet package discovery works when fnm and its Links directory are absent from PATH`` () =
    withTools(fun root _ _ ->
        let package=Path.Combine(root,"Microsoft","WinGet","Packages","Schniz.fnm_Microsoft.Winget.Source_test")
        Directory.CreateDirectory package |> ignore
        let executable=Path.Combine(package,"fnm.exe")
        File.WriteAllText(executable,"")
        /// Supplies the installation root of a stale Windows process environment.
        let environment key=if key="LOCALAPPDATA" then Some root else None
        Assert.Equal(Some executable,findManager true environment (fun _ -> None)))

[<Fact>]
let ``Wrong npm version and failed installation stop preparation with actionable errors`` () =
    withTools(fun _ node _ ->
        /// Simulates a manually upgraded npm beside an otherwise correct Node.
        let wrongNpm command=if command.Arguments=["--version"] then Ok "v20.20.2" else Ok "11.19.1"
        let error=select wrongNpm true requirements (Some node) None |> assertError
        Assert.Contains("Expected npm 10.x",error)
        Assert.Contains("No shell profile setup",error)
        /// Simulates an unavailable download, without proceeding to a build.
        let offline (_:Command)=Error "network unavailable"
        Assert.Contains("network unavailable",select offline true requirements None (Some "fnm") |> assertError))

[<Fact>]
let ``Repository requirements use the exact Node pin and npm major`` () =
    withTools(fun root _ _ ->
        Directory.CreateDirectory(Path.Combine(root,"src","Client")) |> ignore
        File.WriteAllText(Path.Combine(root,".node-version"),"20.20.2\n")
        File.WriteAllText(Path.Combine(root,"src","Client","package.json"),"{\"engines\":{\"node\":\"20.x\",\"npm\":\"10.x\"}}")
        Assert.Equal(requirements,readRequirements root |> assertOk)
        File.WriteAllText(Path.Combine(root,".node-version"),"20\n")
        Assert.Contains("complete Node version",readRequirements root |> assertError))
