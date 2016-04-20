namespace System
open System.Reflection

[<assembly: AssemblyTitleAttribute("FsInteractiveService")>]
[<assembly: AssemblyProductAttribute("FsInteractiveService")>]
[<assembly: AssemblyDescriptionAttribute("F# interactive service API exposed via a lightweight HTTP server")>]
[<assembly: AssemblyVersionAttribute("1.0")>]
[<assembly: AssemblyFileVersionAttribute("1.0")>]
do ()

module internal AssemblyVersionInformation =
    let [<Literal>] Version = "1.0"
    let [<Literal>] InformationalVersion = "1.0"
