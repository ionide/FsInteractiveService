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
  [ yield "<table>"
    for i in 0 .. t.GetLength(0)-1 do
      yield "<tr>"
      for j in 0 .. t.GetLength(1)-1 do
        yield "<td>" + t.[i,j] + "</td>" 
      yield "</tr>"
    yield "</table>" ] |> String.concat "")
#endif
(**
This defines a HTML printer that formats values of type `Table`. Note that this is done based on 
the type of the function - the `AddHtmlPrinter` method takes a function `'a -> string` and it
registers a mapping for values of type `'a`.

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
*)