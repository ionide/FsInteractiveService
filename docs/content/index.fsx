(**
F# Interactive Service
======================

The FsInteractiveService project provides a simple layer over the F# Interactive services 
from the [F# Compiler Services project](http://fsharp.github.io/FSharp.Compiler.Services). It makes the 
service available as a stand-alone process that can be started and called via HTTP requests. It is very
similar to the [FsAutoComplete](https://github.com/fsharp/FsAutoComplete/) project, which provides similar
out-of-process wrapper for F# Compiler IDE services.

The FsInteractiveService project can be used to build F# Interactive editor integration for editors
that are not based on .NET such as Atom. For example, the following shows some of the features of the
F# Interactive integration in [Ionide](http://ionide.io).

<img src="img/atom.png" style="width:80%; margin:10px 10% 10px 10%" />

Documentation
-------------

 * [Creating HTML printers](htmlprinter.html) discusses an extension that F# Interactive Service provides
   for formatting values as HTML objects. This can be done by registering a printer using `fsi.AddHtmlPrinter`.

 * [Calling FsInteractiveService via HTTP](http.html) shows how to start the `FsInteractiveService.exe` process
   in background and how to communicate with it using REST-based API over network. It shows different commands
   you can send and responses you'll get back.
 
Contributing and copyright
--------------------------

The project is hosted on [GitHub][gh] where you can [report issues][issues], fork 
the project and submit pull requests. If you're adding a new public API, please also 
consider adding [samples][content] that can be turned into a documentation. You might
also want to read the [library design notes][readme] to understand how it works.

The library is available under Public Domain license, which allows modification and 
redistribution for both commercial and non-commercial purposes. For more information see the 
[License file][license] in the GitHub repository. 

  [content]: https://github.com/fsprojects/FsInteractiveService/tree/master/docs/content
  [gh]: https://github.com/fsprojects/FsInteractiveService
  [issues]: https://github.com/fsprojects/FsInteractiveService/issues
  [readme]: https://github.com/fsprojects/FsInteractiveService/blob/master/README.md
  [license]: https://github.com/fsprojects/FsInteractiveService/blob/master/LICENSE.txt
*)
