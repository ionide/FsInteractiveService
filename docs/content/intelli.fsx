(*** hide ***)
#r "../../packages/test/FSharp.Data/lib/net40/FSharp.Data.dll"
open FSharp.Data
open System.Diagnostics
let root = __SOURCE_DIRECTORY__ + "/../../bin/FsInteractiveService/"

let fsiservice =
  ProcessStartInfo
    ( FileName = root + "FsInteractiveService.exe",
      Arguments="18083",
      CreateNoWindow=true,UseShellExecute=false )
  |> Process.Start
(**
Getting IntelliSense via HTTP API
=================================

This page gives a very brief overview of the IntelliSense commands exposed by the
`FsInteractiveService.exe` server. The server exposes endpoints:

 - `/completion` - get auto-completion list at the specified location
 - `/tooltip` - get tooltip for item from previously obtained auto-completion list
 - `/paramhints` - get hint method parameters (on opening bracket)
 - endpoints for evaluating snippets [are discussed on a separate page](http.html)

In this tutorial, we assume we already have the `FsInteractiveService.exe` process running
on port `18083`. For more information about starting the server, see [running snippets using
FsInteractiveService via HTTP](http.html).

Getting completion on namespaces
--------------------------------

The `/completion` command can be used to get auto-completion information in F# Interactive.
For example, if the user types `Microsoft.FSharp.`, we can get the completion on the
types and other entities in the namespace.. For that, call `/completion` with body
containing `sourceLine` (with the text on the current line of the input) and `column`
(1-based index of the `.` in the string). The following returns multiple namespaces, but
we show only first 3 in the output:
*)
(*** define-output:it1 ***)
Http.RequestString
  ( "http://localhost:18083/completion", httpMethod="POST",
    body=TextRequest """{ "column":17,
      "sourceLine":"Microsoft.FSharp.Collections" }""")
(*** include-it:it1 ***)
(**
Getting completion on previous symbols
--------------------------------------

The `/completion` command also works on symbols defined in previous interactions. For
example, we can call `/eval` to evaluate `let nums = [| 1 .. 10 |]` and then get completion
on the `nums.` value:

*)
(*** define-output:it2 ***)
Http.RequestString
  ( "http://localhost:18083/eval", httpMethod="POST",
    body=TextRequest """{ "file":"/a.fsx", "line":10,
      "code":"let nums = [| 1 .. 10 |]"}""")

Http.RequestString
  ( "http://localhost:18083/completion", httpMethod="POST",
    body=TextRequest """{ "column":5, "sourceLine":"nums.L" }""")
(*** include-it:it2 ***)
(**
We only show the first 3 elements of the response here, but you can see that it contains
methods that are only available on array object - in particular, the `CopyTo` and
`Clone` methods come from array (while `Equals` comes from `System.Object`).

Getting tooltips in completion list
-----------------------------------

After you call `/completion`, the FsInteractiveService process remembers the symbols
that it provided and it lets you get additional tool tip information for individual
symbols in the list. For example, the following performs auto-completion on
`List.` and then it looks for detailed information about the `map` function:
*)
(*** define-output:it3 ***)
Http.RequestString
  ( "http://localhost:18083/completion", httpMethod="POST",
    body=TextRequest """{ "column":5, "sourceLine":"List." }""")

Http.RequestString
  ( "http://localhost:18083/tooltip", httpMethod="POST",
    body=TextRequest """{ "filter":"map" }""")
(*** include-it:it3 ***)
(**
The value passed to the `filter` parameter has to be a full name of one of the items in the
previously returned completion list. If the item is not found, the result is a JSON value `null`.

In the above response, the `doc` field returns a record with `key` and `fileName` that
can be used to lookup the documentation. This may also be `null` or a field containing the
inline XML documentation. To show the last case, the following defines a function `hiThere`
with a `///` documentation comment, finds it in completion list and gets its tooltip:
*)
(*** define-output:it4 ***)
Http.RequestString
  ( "http://localhost:18083/eval", httpMethod="POST",
    body=TextRequest """{ "file":"/a.fsx", "line":10,
      "code":"/// Hi there!\nlet hiThere() = 1"}""")

Http.RequestString
  ( "http://localhost:18083/completion", httpMethod="POST",
    body=TextRequest """{ "column":1, "sourceLine":"hi" }""")

Http.RequestString
  ( "http://localhost:18083/tooltip", httpMethod="POST",
    body=TextRequest """{ "filter":"hiThere" }""")
(*** include-it:it4 ***)
(**
Getting parameter hints
-----------------------

The `/paramhints` is the last command provided by the service.
It returns information about method overloads. This can be
used to show a tool tip when typing, for example, `Console.WriteLine(`. This is exactly what the
following snippet shows. Here, the `column` value is a 1-based index of the opening parenthesis:
*)
(*** define-output:it5 ***)
Http.RequestString
  ( "http://localhost:18083/paramhints", httpMethod="POST",
    body=TextRequest """{ "column":25,
      "sourceLine":"System.Console.WriteLine(123)" }""")
(*** include-it:it5 ***)

(*** hide ***)
fsiservice.Kill()
