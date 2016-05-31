module FsInteractiveService.Main

open System
open System.IO
open System.Text
open System.Threading
open Microsoft.FSharp.Compiler
open Microsoft.FSharp.Compiler.SourceCodeServices
open Microsoft.FSharp.Compiler.Interactive.Shell
open FsInteractiveService.Shared

// ------------------------------------------------------------------------------------------------
// Types that are exposed via REST API
// ------------------------------------------------------------------------------------------------

/// Request data for /eval
type EvaluationRequest = 
  { file : string
    line : int
    code : string }

/// Request data for /paramhints and /completion
type LineColumnRequest = 
  { sourceLine : string 
    column : int }

/// Request dta for /tooltip
type ToolTipRequest =   
  { filter : string }


/// Response data for /eval
type Result =
  { result : string
    output : string
    details : obj (* unit + EvalDetails + TypeCheckError[] *) }

type TypeCheckError = 
  { startLine : int
    endLine : int
    startColumn : int
    endColumn : int 
    fileName : string
    severity : string 
    errorNumber : int
    message : string }

type HtmlKeyValue = 
  { key : string
    value : string }

type HtmlResult =
  { body : string
    parameters : HtmlKeyValue[] }

type EvalDetails =
  { string : string
    html : HtmlResult
    warnings : TypeCheckError[] }


/// Reply to /tooltip
type ToolTipResponse = 
  { signature:string
    doc:obj (* null + FullXmlDoc + LookupXmlDoc *)
    footer:string }

type FullXmlDoc =  
  { xmldoc : string }

type LookupXmlDoc =   
  { key : string
    fileName : string }


// Reply to /paramhints
type ParameterToolTip =
  { signature : string
    doc : obj (* null + FullXmlDoc + LookupXmlDoc *)
    parameters : string[] }


// ------------------------------------------------------------------------------------------------
// F# Compiler Service Interop
// ------------------------------------------------------------------------------------------------

type FsiSession =
  { Output : StringBuilder
    Session : FsiEvaluationSession }
    

/// Extend the `fsi` object with `fsi.AddHtmlPrinter` 
let addHtmlPrinter = """
  module FsInteractiveService = 
    let mutable htmlPrinters = []
    let tryFormatHtml o = htmlPrinters |> Seq.tryPick (fun f -> f o)
    let htmlPrinterParams = System.Collections.Generic.Dictionary<string, obj>()
    do htmlPrinterParams.["html-standalone-output"] <- false

  type Microsoft.FSharp.Compiler.Interactive.InteractiveSession with
    member x.HtmlPrinterParameters = FsInteractiveService.htmlPrinterParams
    member x.AddHtmlPrinter<'T>(f:'T -> seq<string * string> * string) = 
      FsInteractiveService.htmlPrinters <- (fun (value:obj) ->
        match value with
        | :? 'T as value -> Some(f value)
        | _ -> None) :: FsInteractiveService.htmlPrinters"""

/// Start the F# interactive session with HAS_FSI_ADDHTMLPRINTER symbol defined
let startSession () = 
    let sbOut = new StringBuilder()
    let inStream = new StringReader("")
    let outStream = new StringWriter(sbOut)
    let errStream = new StringWriter(sbOut)

    let argv = [| "/tmp/fsi.exe" |]
    let allArgs = Array.append argv [|"--noninteractive"; "--define:HAS_FSI_ADDHTMLPRINTER" |]

    let fsiConfig = FsiEvaluationSession.GetDefaultConfiguration(Microsoft.FSharp.Compiler.Interactive.Shell.Settings.fsi, false)
    let fsiSession = FsiEvaluationSession.Create(fsiConfig, allArgs, inStream, outStream, errStream) 
    
    // Report unhandled background exceptions to the output stream
    AppDomain.CurrentDomain.UnhandledException.Add(fun ex ->
        sbOut.AppendLine(ex.ExceptionObject.ToString()) |> ignore )

    // Load the `fsi` object from the right location of the `FSharp.Compiler.Interactive.Settings.dll`
    // assembly and add the `fsi.AddHtmlPrinter` extension method; then clean it from FSI output
    let origLength = sbOut.Length

    let interactiveSessionLocation = typeof<Microsoft.FSharp.Compiler.Interactive.InteractiveSession>.Assembly.Location
    let fsiLocation                = Microsoft.FSharp.Compiler.Interactive.Shell.Settings.fsi.GetType().Assembly.Location

    fsiSession.EvalInteraction("#r @\"" + interactiveSessionLocation + "\"")
    fsiSession.EvalInteraction("#r @\"" + fsiLocation + "\"")
    fsiSession.EvalInteraction(addHtmlPrinter)
    sbOut.Remove(origLength, sbOut.Length-origLength) |> ignore

    Console.SetOut(new StringWriter(sbOut))
    { Output = sbOut; Session = fsiSession }


/// Helper for extracting results from a two element tuple returned by FSI session
let (|FsiTupleResult|_|) (res:FsiValue) = 
  match Reflection.FSharpValue.GetTupleFields(res.ReflectionValue) with
  | [| v1; v2 |] -> Some(v1, v2)
  | _ -> None

let convertErrors (errors:FSharpErrorInfo[]) = 
    [| for e in errors -> 
        { startLine = e.StartLineAlternate; endLine = e.EndLineAlternate; startColumn = e.StartColumn
          endColumn = e.EndColumn; fileName = e.FileName; errorNumber = e.ErrorNumber; message = e.Message
          severity = if e.Severity = FSharpErrorSeverity.Error then "error" else "warning" } |]

/// Remove whitespace from the beginning of the snippet. For example, given
/// "  let a = 10\n  a + a", we drop two spaces and get just "let a = 10\na + a"
let dropTrailingWhiteSpace code = 
    // Split string into lines using StreamReader (which handles all odd \r\n combos)
    let lines = 
      [ use sr = new StringReader(code)
        let line = ref (sr.ReadLine())
        while line.Value <> null do 
          yield line.Value
          line := sr.ReadLine() ]

    // Count number of spaces on non-empty lines & drop them
    let spaces = 
      lines 
      |> Seq.filter (String.IsNullOrWhiteSpace >> not) 
      |> Seq.map (fun s -> s.Length - s.TrimStart(' ').Length) 
      |> Seq.fold min Int32.MaxValue
    lines 
    |> Seq.map (fun l ->
        if String.IsNullOrWhiteSpace l then l
        else l.Substring(spaces))
    |> String.concat "\n"

/// Evaluate interaction and return exception/error/success, possibly with formatted HTML value
let evaluateInteraction file line (code:string) session = 
    let dir = Path.GetDirectoryName(file)
    let allcode = sprintf "#silentCd @\"%s\"\n# %d @\"%s\"\n%s" dir line file (dropTrailingWhiteSpace code)
    let res = session.Session.EvalInteractionNonThrowing(allcode)
    let output = session.Output.ToString()
    session.Output.Clear() |> ignore

    match res with
    | _, errors when errors |> Seq.exists (fun e -> e.Severity = FSharpErrorSeverity.Error) ->
        { result = "error"
          output = output
          details = convertErrors errors }

    | Choice2Of2 exn, _ ->
        { result = "exception"
          output = output
          details = exn.ToString() }    

    | Choice1Of2 (), warnings ->
        let itval = session.Session.EvalExpressionNonThrowing("it, FsInteractiveService.tryFormatHtml it")
        session.Session.EvalInteraction("(null:obj)")
        session.Output.Clear() |> ignore
        match itval with
        | Choice1Of2(Some(FsiTupleResult(itval, (:? option<seq<string * string> * string> as html)))), _ when itval <> null ->
            let html = 
              match html with
              | Some(ps, body) -> { parameters = [| for k, v in ps -> { key = k; value = v } |]; body = body }
              | None -> Unchecked.defaultof<_> (* null in JSON *)
            { result = "success"; output = output; 
              details = { string = itval.ToString(); html = html; warnings = convertErrors warnings } } 
        | _ -> 
            let html = Unchecked.defaultof<_> (* null in JSON *)
            { result = "success"; output = output; 
              details = { string = null; html = html; warnings = convertErrors warnings } } 


// ------------------------------------------------------------------------------------------------
// Background agent that handles F# Interactive Service requests
// ------------------------------------------------------------------------------------------------

type AgentMessage = 
    | ReadOutput of AsyncReplyChannel<Result>
    | Evaluate of EvaluationRequest * AsyncReplyChannel<Choice<Result, exn>>
    | Completion of LineColumnRequest * AsyncReplyChannel<CompletionData[]>
    | ParameterHints of LineColumnRequest * AsyncReplyChannel<ParameterToolTip[]>
    | Tooltip of ToolTipRequest * AsyncReplyChannel<ToolTipResponse option>
    | Reset 
    | Cancel
    | Done

/// Turns XML doc into `null + FullXmlDoc + LookupXmlDoc` for JSON encoding
let boxXmlDoc doc = 
    match doc with 
    | XmlDoc.EmptyDoc -> null
    | XmlDoc.Full s -> box { xmldoc = s }
    | XmlDoc.Lookup(k, Some f) -> box { key = k; fileName = f }
    | XmlDoc.Lookup(k, None) -> box { key = k; fileName = null }

let agent = MailboxProcessor.Start(fun inbox -> 
    let queue = System.Collections.Generic.Queue()
    let rec running symbols thread session = async {

        // Run operation from the queue, or wait for the next message
        let! msg = 
            if queue.Count > 0 && thread = None then async.Return(Evaluate(queue.Dequeue()))
            else inbox.Receive()
        
        match msg, thread, symbols with
        // Cancel a thread evaluating the last interaction
        | Cancel, Some (t:Thread), _ ->
            t.Abort()
            return! running None None session 

        // Thread completed or cancelling but no thread is running
        | Done, _, _ | Cancel, None, _ ->
            return! running None None session 

        // Read F# Interactive output 
        | ReadOutput repl, _, _ ->
            let output = session.Output.ToString()
            session.Output.Clear() |> ignore
            repl.Reply({ result = "output"; output = output; details = null })
            return! running None thread session

        // Reset F# Interactive session
        | Reset, _, _ ->
            thread |> Option.iter (fun t -> t.Abort())
            return! running None None (startSession())

        // Evaluate request, but we have a thread in background -> enqueue
        | Evaluate(req, repl), Some _, _ ->
            queue.Enqueue(req, repl)
            return! running None thread session 

        // Evaluate request and no background processing is running
        | Evaluate(req, repl), None, _ ->
            let t = Thread(fun () ->
                try
                  try
                    let res = evaluateInteraction req.file req.line req.code session
                    repl.Reply(Choice1Of2 res) 
                  with e ->
                    repl.Reply(Choice2Of2 e)
                finally inbox.Post(Done) )
            t.Start()
            return! running None (Some t) session 
        
        // IntelliSense - handle autocompletion request
        | Completion(req, repl), _, _ ->
            let! symbols, results = Completion.getCompletions(session.Session, req.sourceLine, req.column)
            repl.Reply(results)
            return! running (Some symbols) thread session 
        
        // IntelliSense - handle parameter hints
        | ParameterHints(req, repl), _, _ ->
            let! results = Completion.getParameterHints(session.Session, req.sourceLine, req.column)
            let result = results |> List.choose (function 
                | ParameterTooltip.EmptyTip -> None
                | ParameterTooltip.ToolTip(sgn, doc, pars) ->
                    Some { signature = sgn; doc = boxXmlDoc doc; parameters = Array.ofSeq pars })
            repl.Reply(Array.ofSeq result)
            return! running None thread session 

        // IntelliSense - handle tooltip (only after completion!)
        | Tooltip(_, repl), _, None ->
            repl.Reply(None) 
            return! running None thread session 

        | Tooltip(req, repl), _, Some symbols ->
            let! tooltip = Completion.getCompletionTooltip symbols req.filter
            let result = 
                match tooltip with
                | ToolTips.ToolTip(sgn, doc, ft) -> 
                    Some { signature = sgn; doc = boxXmlDoc doc; footer = ft }
                | ToolTips.EmptyTip -> Some(Unchecked.defaultof<_>) (* Some(null) *)
            repl.Reply(result) 
            return! running (Some symbols) thread session }

    running None None (startSession()))


// ------------------------------------------------------------------------------------------------
// Serializing F# records using Newtonsoft.Json
// ------------------------------------------------------------------------------------------------

open Suave
open Suave.Http
open Suave.Filters
open Suave.Operators

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


let handleRequest op ctx = async {
    let! result = op ctx.request
    return! ctx |> Successful.OK (NJson.toJson result) }

// ------------------------------------------------------------------------------------------------
// Expose everything via lightweight Suave server
// ------------------------------------------------------------------------------------------------

let app =
    Writers.setMimeType "application/json; charset=utf-8" >=>
    POST >=> choose [
        // Returns:
        //  - Result<TypeCheckError[]> when result = "error" 
        //  - Result<string>           when result = "exception"
        //  - Result<EvalDetails>      when result = "success"
        path "/eval" >=> request (fun request ctx -> async {
            let req = NJson.fromJson (Encoding.UTF8.GetString request.rawForm)
            let! res = agent.PostAndAsyncReply(fun ch -> Evaluate(req, ch))
            match res with 
            | Choice1Of2 result -> return! Successful.OK (NJson.toJson result) ctx
            | Choice2Of2 exn -> return! ServerErrors.INTERNAL_ERROR (exn.ToString()) ctx })
        

        // Returns: Result<unit>  with result = "output"
        path "/output" >=> handleRequest (fun _ -> 
            agent.PostAndAsyncReply(ReadOutput))

        path "/reset" >=> handleRequest (fun _ -> async {
            agent.Post(Reset)
            return { result = "reset"; output=""; details = null }})

        path "/cancel" >=> handleRequest (fun _ -> async {
            agent.Post(Cancel)
            return { result = "canceled"; output=""; details = null }})         


        // Returns: ToolTipResponse which may be 'null' or BAD_REQUEST if /completion not called first
        path "/tooltip" >=> request (fun request ctx -> async {
            let req = NJson.fromJson (Encoding.UTF8.GetString request.rawForm)
            let! res = agent.PostAndAsyncReply(fun ch -> Tooltip(req, ch))
            match res with
            | Some result -> return! Successful.OK (NJson.toJson result) ctx
            | None -> return! RequestErrors.BAD_REQUEST "Call /completion first" ctx  })

        // Returns: CompletionData[]            
        path "/completion" >=> handleRequest (fun r -> async {
            let req = NJson.fromJson (Encoding.UTF8.GetString r.rawForm)
            let! res = agent.PostAndAsyncReply(fun ch -> Completion(req, ch))
            return res })

        // Returns: ???
        path "/paramhints" >=> handleRequest (fun r -> async {
            let req = NJson.fromJson (Encoding.UTF8.GetString r.rawForm)
            let! res = agent.PostAndAsyncReply(fun ch -> ParameterHints(req, ch))
            return res })   ]


[<EntryPoint>]
let main argv =
    let binding =
        let create address port = HttpBinding.mkSimple HTTP (defaultArg address "127.0.0.1") (defaultArg port 8707)
        let parsePort port = match Int32.TryParse port with | true, port -> Some port | _ -> None

        if argv.Length = 0 then
            create None None
        else
            match argv.[0].Split(':') with
            | [|""; port|] -> create None (parsePort port)
            | [|address; port|] -> create (Some address) (parsePort port)
            | [|port|] when fst(Int32.TryParse(port)) = true -> create None (parsePort port)
            | [|address|] -> create (Some address) None
            | _ -> failwith (sprintf "Invalid command line parameter '%s'" argv.[0])

    let serverConfig = { defaultConfig with bindings = [binding]}
    startWebServer serverConfig app
    0