#if INTERACTIVE
#r "../../packages/test/NUnit/lib/nunit.framework.dll"
#r "../../packages/test/FSharp.Data/lib/net40/FSharp.Data.dll"
#r "../../packages/test/FsUnit/lib/net45/FsUnit.NUnit.dll"
#I "../../src/FsInteractiveService/bin/Debug"
#r "Suave.dll"
#r "FsInteractiveService.exe"
#else
module FsInteractiveService.Tests
#endif

open FsUnit
open NUnit.Framework
open FsInteractiveService
open FsInteractiveService.Main
open FSharp.Data

open System
open Suave.Http
open Suave.Sockets
open System.Threading

// ------------------------------------------------------------------------------------------------
// Suave testing helpers
// ------------------------------------------------------------------------------------------------


let makeContext path = 
    let req = { HttpRequest.empty with url = Uri("http://127.0.0.1" + path); ``method`` = HttpMethod.POST }
    HttpContext.mk req HttpRuntime.empty Connection.empty false

let withContent json ctx = 
    let text = NJson.toJson json
    { ctx with request = { ctx.request with rawForm = Text.Encoding.UTF8.GetBytes text } }

let startRequest (app:WebPart) ctx = 
    app ctx |> Async.Ignore |> Async.Start

let asyncGetResponse (app:WebPart) ctx = async {
    let! res = ctx |> app 
    Assert.IsTrue(res.IsSome, sprintf "Got no response for '%s'." (ctx.request.url.ToString()))
    match res.Value.response.content with
    | HttpContent.Bytes bytes -> return Text.Encoding.UTF8.GetString bytes
    | _ -> 
        Assert.Fail(sprintf "Got no content for '%s'." (ctx.request.url.ToString()))
        return failwith "Assert" }

let getResponse (app:WebPart) ctx = 
    asyncGetResponse app ctx |> Async.RunSynchronously

// ------------------------------------------------------------------------------------------------
// Basic tests
// ------------------------------------------------------------------------------------------------

type ErrorResult = JsonProvider<"""{"result":"error","output":"Stopped due to error\n","details":
  [{"startLine":666,"endLine":666,"startColumn":6,"endColumn":7,"fileName":"/test.fsx",
    "severity":"error","errorNumber":1,"message":"The type 'int' does not match the type 'float'"},
   {"startLine":666,"endLine":666,"startColumn":4,"endColumn":5,"fileName":"/test.fsx",
    "severity":"error","errorNumber":43,"message":"The type 'int' does not match the type 'float'"}]}""">

[<Test>]
let ``Reset returns "F# Interactive" welcome message`` () =
  makeContext "/reset"
  |> getResponse Main.app 
  |> ignore

  makeContext "/output"
  |> getResponse Main.app 
  |> should contain "F# Interactive"

[<Test>]
let ``Eval "1+1" should return output "val it : int = 2"`` () =
  makeContext "/eval"
  |> withContent { file = "/test.fsx"; line = 10; code = "1+1" } 
  |> getResponse Main.app
  |> should contain "val it : int = 2"

[<Test>]
let ``Eval 'printfn "Hi"' captures the console output`` () =
  makeContext "/eval"
  |> withContent { file = "/test.fsx"; line = 10; code = """printfn "hello world" """ } 
  |> getResponse Main.app
  |> should contain "hello world"

[<Test>]
let ``Type checking errors have correct line number`` () =
  let errors =
    makeContext "/eval"
    |> withContent { file = "/test.fsx"; line = 665; code = "let n = 1\n1.0 + n" } 
    |> getResponse Main.app
    |> ErrorResult.Parse
  errors.Result |> should equal "error"
  errors.Details.[0].StartLine |> should equal 666

[<Test>]
let ``Start 'while true do ()' and cancel it afterwards`` () =
  let loop =
    makeContext "/eval"
    |> withContent { file = "/test.fsx"; line = 10; code = """while true do ()""" } 
    |> asyncGetResponse Main.app
    |> Async.StartAsTask

  Thread.Sleep(100)  
  let add = 
    makeContext "/eval"
    |> withContent { file = "/test.fsx"; line = 10; code = """40+2""" } 
    |> asyncGetResponse Main.app
    |> Async.StartAsTask

  Thread.Sleep(100)
  let cancel =
    makeContext "/cancel"
    |> asyncGetResponse Main.app 
    |> Async.StartAsTask

  loop.Result |> should contain "OperationCanceledException"
  add.Result |> should contain "42"
  cancel.Result |> should contain "canceled"