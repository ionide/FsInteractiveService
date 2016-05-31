(**
Creating HTML printers
======================

The FsInteractiveService project extends F# Interactive with a feature that makes
it possible to format objects as HTML. This follows very similar pattern to the
`fsi.AddPrinter` function available in F# by default.

The code that gets sent to FsInteractiveService can use an extension `fsi.AddHtmlPrinter`.
The process also defines a `HAS_FSI_ADDHTMLPRINTER` symbol, so that you can detect whether
the feature is available.

Formatting HTML tables
----------------------
As an example, consider the following type that defines a very simple table of strings:
*)
type Table = Table of string[,]
(**
The typical pattern for registering a printer would be to call `fsi.AddHtmlPrinter` wrapped
inside an `#if .. #endif` block for compatibility reasons:
*)
#if HAS_FSI_ADDHTMLPRINTER
fsi.AddHtmlPrinter(fun (Table t) ->
  let body = 
    [ yield "<table>"
      for i in 0 .. t.GetLength(0)-1 do
        yield "<tr>"
        for j in 0 .. t.GetLength(1)-1 do
          yield "<td>" + t.[i,j] + "</td>" 
        yield "</tr>"
      yield "</table>" ] 
    |> String.concat ""
  seq [ "style", "<style>table { background:#f0f0f0; }</style>" ],
  body )
#endif
(**
This defines a HTML printer that formats values of type `Table`. The function passed to 
`AddHtmlPrinter` needs to return a value of type `seq<string*string> * string`. This is
a tuple consisting of two things:

 - The second element is the HTML body that represents the formatted value. Typically,
   editors will embed this into HTML output.

 - A sequence of key value pairs that represents additional styles and sripts used that
   are required by the body. The keys can be `style` or `script` (or other custom keys
   supported by editors) and can be treated in a special way by the editors (e.g. if
   loading JavaScript dynamically requires placing the HTML content in an `<iframe>`).

Note that this is done based on the type of the function - the `AddHtmlPrinter` 
method takes a function `'a -> _` and it registers a mapping for values of type `'a`.

You can now define a table as follows:
*)
let table = 
  [ [ "Test"; "More"]
    [ "1234"; "5678"] ]
  |> array2D |> Table    
(**
In the current version, the value is only formatted when `Table` is returned as a direct result of
an expression. This means that you need to evaluate an expression of type `Table` rather than,
for example, a value binding as above.

To see the table formatted run:
*)
table
(**
If you are [calling FsInteractiveService via HTTP](http.html), then the formatted HTML will be
returned as part of the resulting JSON - inside the `html` field of the `details` fields of the
returned JSON.

Specifying HTML formatting parameters
-------------------------------------

In addition to the `fsi.AddHtmlPrinter` function, the FsInteractiveService also provides a
way of specifying HTML formatting parameters. This may be used by editors and libraries to 
configure how printing of different elements is done.

One parameter that is specified by the FsInteractiveService (with default value `false`) is
`html-standalone-output`. This specifies whether the produced HTML should be standalone HTML
that does not require any background service (and can be saved locally). The `false` value
means that formatters can start background HTTP servers and call them from the formatted HTML
code (e.g. to load data on the fly). To access the parameter, use:
*)
#if HAS_FSI_ADDHTMLPRINTER
let standaloneHtmlOutput = 
  fsi.HtmlPrinterParameters.["html-standalone-output"] :?> bool
#endif