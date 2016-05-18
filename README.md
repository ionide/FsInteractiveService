[![Travis build status](https://travis-ci.org/ionide/FsInteractiveService.png)](https://travis-ci.org/ionide/FsInteractiveService)

# FsInteractiveService

The FsInteractiveService project provides a simple layer over the F# Interactive services
from the [F# Compiler Services project](http://fsharp.github.io/FSharp.Compiler.Services). It makes the
service available as a stand-alone process that can be started and called via HTTP requests. It is very
similar to the [FsAutoComplete](https://github.com/fsharp/FsAutoComplete/) project, which provides similar
out-of-process wrapper for F# Compiler IDE services.

The FsInteractiveService project can be used to build F# Interactive editor integration for editors
that are not based on .NET such as Atom.

Documentation
-------------

 * [F# Interactive Service home](http://ionide.io/FsInteractiveService) is the homepage for the project.
   Go here to get started with FsInteractiveService.

 * [Creating HTML printers](http://ionide.io/FsInteractiveService/htmlprinter.html) discusses an extension that F# Interactive Service provides
   for formatting values as HTML objects. This can be done by registering a printer using `fsi.AddHtmlPrinter`.

 * [Calling FsInteractiveService via HTTP](http://ionide.io/FsInteractiveService/http.html) shows how to start the `FsInteractiveService.exe` process
   in background and how to communicate with it using REST-based API over network. It shows different commands
   you can send and responses you'll get back.
