namespace System
open System.Reflection

[<assembly: AssemblyTitleAttribute("FsInteractiveService.Shared")>]
[<assembly: AssemblyProductAttribute("FsInteractiveService")>]
[<assembly: AssemblyDescriptionAttribute("F# interactive service API exposed via a lightweight HTTP server")>]
[<assembly: AssemblyVersionAttribute("0.1.2")>]
[<assembly: AssemblyFileVersionAttribute("0.1.2")>]
do ()

module internal AssemblyVersionInformation =
    let [<Literal>] Version = "0.1.2"
    let [<Literal>] InformationalVersion = "0.1.2"
