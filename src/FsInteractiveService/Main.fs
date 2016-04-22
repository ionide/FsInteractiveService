module FsInteractiveService.Main

open System
open System.IO
open System.Text
open Microsoft.FSharp.Compiler
open Microsoft.FSharp.Compiler.SourceCodeServices
open Microsoft.FSharp.Compiler.Interactive.Shell

module NJson = 
    let private json = Newtonsoft.Json.JsonSerializer.Create()
    let fromJson<'T> str = 
        use sr = new StringReader(str)
        json.Deserialize(sr, typeof<'T>) :?> 'T

    let toJson (obj:'T) = 
        let sb = StringBuilder()
        ( use sw = new StringWriter(sb)
          json.Serialize(sw, obj) )
        sb.ToString()

// ------------------------------------------------------------------------------------------------
// F# Compiler Service Interop
// ------------------------------------------------------------------------------------------------

type TypeCheckError = 
  { startLine : int
    endLine : int
    startColumn : int
    endColumn : int 
    fileName : string
    severity : string 
    errorNumber : int
    message : string }

type Result =
  { result : string
    output : string
    details : obj }

type ItValue =
  { string : string
    html : string }

type FsiSession =
  { Output : StringBuilder
    Session : FsiEvaluationSession }

let addHtmlPrinter = """
  module FsInteractiveService = 
    let htmlPrinters = ResizeArray<_>()
    let tryFormatHtml o = htmlPrinters |> Seq.tryPick (fun f -> f o)

  type Microsoft.FSharp.Compiler.Interactive.InteractiveSession with
    member x.AddHtmlPrinter<'T>(f:'T -> string) = 
      FsInteractiveService.htmlPrinters.Add(fun (value:obj) ->
        match value with
        | :? 'T as value -> Some(f value)
        | _ -> None)"""

let startSession () = 
    let sbOut = new StringBuilder()
    let inStream = new StringReader("")
    let outStream = new StringWriter(sbOut)
    let errStream = new StringWriter(sbOut)

    let argv = [| "/tmp/fsi.exe" |]
    let allArgs = Array.append argv [|"--noninteractive"; "--define:HAS_FSI_ADDHTMLPRINTER" |]

    let fsiConfig = FsiEvaluationSession.GetDefaultConfiguration(Microsoft.FSharp.Compiler.Interactive.Settings.fsi)
    let fsiSession = FsiEvaluationSession.Create(fsiConfig, allArgs, inStream, outStream, errStream) 
    let origLength = sbOut.Length
    fsiSession.EvalInteraction(addHtmlPrinter)
    sbOut.Remove(origLength, sbOut.Length-origLength) |> ignore
    Console.SetOut(new StringWriter(sbOut))
    { Output = sbOut; Session = fsiSession }

let evaluateInteraction file line code session = 
    let dir = Path.GetDirectoryName(file)
    let allcode = sprintf "#silentCd @\"%s\"\n# %d @\"%s\"\n%s" dir line file code
    let res = session.Session.EvalInteractionNonThrowing(allcode)
    let output = session.Output.ToString()
    session.Output.Clear() |> ignore

    match res with
    | _, errors when errors.Length > 0 ->
        { result = "error"
          output = output
          details = 
            [| for e in errors -> 
                { startLine = e.StartLineAlternate; endLine = e.EndLineAlternate; startColumn = e.StartColumn
                  endColumn = e.EndColumn; fileName = e.FileName; errorNumber = e.ErrorNumber; message = e.Message
                  severity = if e.Severity = FSharpErrorSeverity.Error then "error" else "warning" } |] }

    | Choice2Of2 exn, _ ->
        { result = "exception"
          output = output
          details = exn.ToString() }    

    | Choice1Of2 (), _ ->
        let itval = session.Session.EvalExpressionNonThrowing("it")
        session.Output.Clear() |> ignore
        match itval with
        | Choice1Of2(Some res), _ when res.ReflectionValue <> null -> 
            let it = res.ReflectionValue          
            let meth = it.GetType().GetMethod("ToHtml")
            let html = if meth <> null then meth.Invoke(it, [| |]).ToString() else null
            { result = "success"; output = output; details = { string = it.ToString(); html = html } } 
        | _ -> { result = "success"; output = output; details = { string = null; html = null } } 


type EvaluateRequest = 
  { File : string
    Line : int
    Code : string
    Reply : AsyncReplyChannel<Choice<Result, exn>> }

type AgentMessage = 
  | ReadOutput of AsyncReplyChannel<Result>
  | Evaluate of EvaluateRequest
  | Reset 
  | Cancel
  | Done

open System.Threading

let agent = MailboxProcessor.Start(fun inbox -> 
    let queue = System.Collections.Generic.Queue()
    let rec running thread session = async {

        // Run operation from the queue, or wait for the next message
        let! msg = 
            if queue.Count > 0 && thread = None then async.Return(Evaluate(queue.Dequeue()))
            else inbox.Receive()
        
        match msg, thread with
        // Cancel a thread evaluating the last interaction
        | Cancel, Some (t:Thread) ->
            t.Abort()
            return! running None session 

        // Thread completed or cancelling but no thread is running
        | Done, _ | Cancel, None ->
            return! running None session 

        // Read F# Interactive output 
        | ReadOutput repl, _ ->
            let output = session.Output.ToString()
            session.Output.Clear() |> ignore
            repl.Reply({ result = "output"; output = output; details = null })
            return! running thread session

        // Reset F# Interactive session
        | Reset, _ ->
            thread |> Option.iter (fun t -> t.Abort())
            return! running None (startSession())

        // Evaluate request, but we have a thread in background -> enqueue
        | Evaluate req, Some _ ->
            queue.Enqueue(req)
            return! running thread session 

        // Evaluate request and no background processing is running
        | Evaluate req, None ->
            let t = Thread(fun () ->
                try
                  try
                    let res = evaluateInteraction req.File req.Line req.Code session
                    req.Reply.Reply(Choice1Of2 res) 
                  with e ->
                    req.Reply.Reply(Choice2Of2 e)
                finally inbox.Post(Done) )
            t.Start()
            return! running (Some t) session }

    running None (startSession()))

open Suave
open Suave.Http
open Suave.Filters
open Suave.Operators

let handleRequest op ctx = async {
    let! result = op ctx.request
    return! ctx |> Successful.OK (NJson.toJson result) }

type EvalRequest = 
  { file : string
    line : int
    code : string }

let app =
    Writers.setMimeType "application/json; charset=utf-8" >=>
    POST >=> choose [
        path "/eval" >=> request (fun request ctx -> async {
            let req = NJson.fromJson (Encoding.UTF8.GetString request.rawForm)
            let! res = 
                agent.PostAndAsyncReply(fun ch -> 
                    Evaluate { File = req.file; Line = req.line; Code = req.code; Reply = ch })
            match res with 
            | Choice1Of2 result -> return! Successful.OK (NJson.toJson result) ctx
            | Choice2Of2 exn -> return! ServerErrors.INTERNAL_ERROR (exn.ToString()) ctx })

        path "/output" >=> handleRequest (fun _ -> 
            agent.PostAndAsyncReply(ReadOutput))

        path "/reset" >=> handleRequest (fun _ -> async {
            agent.Post(Reset)
            return { result = "reset"; output=""; details = null }})

        path "/cancel" >=> handleRequest (fun _ -> async {
            agent.Post(Cancel)
            return { result = "canceled"; output=""; details = null }}) ]

[<EntryPoint>]
let main argv =
    let port = try int argv.[0] with _ -> 8707
    let serverConfig = { defaultConfig with bindings = [HttpBinding.mkSimple HTTP "127.0.0.1" port]}
    startWebServer serverConfig app
    0