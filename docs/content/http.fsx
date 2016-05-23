(*** hide ***)
#r "../../packages/test/FSharp.Data/lib/net40/FSharp.Data.dll"
open FSharp.Data
open System.Diagnostics
let root = __SOURCE_DIRECTORY__ + "/../../bin/FsInteractiveService/"
(**
Calling FsInteractiveService via HTTP
=====================================

This page gives a very brief overview of the commands exposed by the `FsInteractiveService.exe` server.
The server exposes endpoints:

 - `/output` for getting output that was printed by F# interactive
 - `/eval` for evaluating expressions and interactions
 - `/cancel` for cancelling running computation
 - `/reset` for restarting the F# Interactive (this is limited though; see below)

Starting the server
-------------------

For the purpose of the demo, we start the server using `Process.Start`. The process takes one optional 
parameter, which is the binding address and port to be used. Either part may also be omitted, in which 
case the defaults are used (`127.0.0.1` and `8707`, respectively).

*)
let fsiservice = 
  ProcessStartInfo
    ( FileName = root + "FsInteractiveService.exe", 
      Arguments=":18082",
      CreateNoWindow=true,UseShellExecute=false )
  |> Process.Start
(**

Reading FSI output
------------------

F# Interactive can print output at any time (e.g. from a background async workflow or after
starting). You can get the output printed since the last time by calling:
*)
(*** define-output:it1 ***)
Http.RequestString
  ("http://localhost:18082/output", httpMethod="POST")
(*** include-it:it1 ***)
(**
In the resulting JSON, the result is set to `output` and you can find the printed message in `output`.

Evaluating code
---------------

To evaluate code, you can call `/eval` with a JSON that contains the file name, line of the source code
in the source file and the code itself (the file does not have to exist).
*)
(*** define-output:it2 ***)
Http.RequestString
  ( "http://localhost:18082/eval", httpMethod="POST", 
    body=TextRequest """{ "file":"/a.fsx", "line":10, "code":"1+1"}""")
(*** include-it:it2 ***)
(**
The result is set to `success` and F# Interactive prints the returned value into `output`.
If the evaluates code is an expression, the `string` field in `details` contains the value formatted
using `ToString`. If your code prints something, you will find the printed result in `output` too:
*)
(*** define-output:it3 ***)
Http.RequestString
  ( "http://localhost:18082/eval", httpMethod="POST", 
    body=TextRequest """{ "file":"/a.fsx", "line":10, "code":"printfn \"Hi!\""}""")
(*** include-it:it3 ***)
(**
Note that this is still an expression (that can be formatted) and so the output includes the `it` value,
but `string` is `null`. Finally, the following snippet evaluates code that throws an exception:
*)
(*** define-output:it3b ***)
Http.RequestString
  ( "http://localhost:18082/eval", httpMethod="POST", 
    body=TextRequest """{ "file":"/a.fsx", "line":10, 
      "code":"1 + failwith \"Oops!\""}""")
(*** include-it:it3b ***)
(**
In this case, the result is `exception` and you can find the formatted exception (using `ToString`) in the
`details` field.

Errors and warnings
-------------------

The following tries to evaluate `1+1.0`, which is a type error:
*)
(*** define-output:it4 ***)
Http.RequestString
  ( "http://localhost:18082/eval", httpMethod="POST", 
    body=TextRequest """{ "file":"/a.fsx", "line":10, 
      "code":"1+1.0"}""")
(*** include-it:it4 ***)
(**
For errors, the result is `errror` and `details` contains an array with detailed information about the 
individual messages. Note that the line number is calculated relatively to the number you provide in the
input. Next, let's look at code that runs, but with warnings:
*)
(*** define-output:it5 ***)
Http.RequestString
  ( "http://localhost:18082/eval", httpMethod="POST", 
    body=TextRequest """{ "file":"/a.fsx", "line":10, 
      "code":"1 + 1:>int"}""")
(*** include-it:it5 ***)
(**
This evaluates fine so result is `success`, but the `details` field contains `warnings` where you can
find an array of warnings (in the same format as errors above).

Cancelling computation
----------------------

If you run an infinite loop in the F# interactive process, you can cancel it by calling `/cancel`.
The following starts a request (in background) with an infinite loop and then cancels it later:
*)
(*** define-output:it6 ***)
async {
  Http.RequestString
    ( "http://localhost:18082/eval", httpMethod="POST", 
      body=TextRequest """{ "file":"/a.fsx", "line":10, 
        "code":"while true do ()"}""") } |> Async.Start

System.Threading.Thread.Sleep(1000)
Http.RequestString
  ("http://localhost:18082/cancel", httpMethod="POST")
(*** include-it:it6 ***)
(**
You can also reset the F# Interactive service by calling `/reset`, but that has limitations.
It resets whatever F# Interactive is doing, but it does not kill all background processes that
it might have started (like `async` workflow started using `Async.Start`), so it is probably better
to just kill the process and restart it completely.

Adding HTML printers
--------------------

As [discussed in HTML printer docs](htmlprinter.html), you can use `AddHtmlPrinter` to register
printer for formatting values as HTML. The FsInteractiveService defines a special symbol 
`HAS_FSI_ADDHTMLPRINTER` that can be used to call the method only when running in the 
FsInteractiveService context.

The method registers printers based on types. For example, the following silly example defines
a printer that makes integers bold:
*)
(*** define-output:it7 ***)
Http.RequestString
  ( "http://localhost:18082/eval", httpMethod="POST", 
    body=TextRequest """{ "file":"/a.fsx", "line":10,   
      "code":"fsi.AddHtmlPrinter(fun (n:int) -> sprintf \"<b>%d</b>\" n)"}""")
(**
This evaluates without returning anything interesting. Now, we can evaluate an expression
that returns an `int`:
*)
(*** define-output:it8 ***)
Http.RequestString
  ( "http://localhost:18082/eval", httpMethod="POST", 
    body=TextRequest """{ "file":"/a.fsx", "line":10, 
      "code":"42"}""")
(*** include-it:it8 ***)
(**
When HTML printer is registered for a type, the `details` field will incldue the result of calling the
HTML printer in the `html` field. You can see that we got `"<b>42</b>"` here!

Wrapping up
-----------
Do not forget to kill the `FsInteractiveService.exe` process at the end...
*)
fsiservice.Kill()
