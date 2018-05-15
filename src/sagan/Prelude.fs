[<AutoOpen>]
module internal Sagan.Prelude

open System
open System.Threading
open System.Threading.Tasks
open FSharp.Control

/// Maps over individual items of a pair.
let inline mapPair f g (a,b) = (f a, g b)

module Option =
  /// Given a default value and an option, returns the option value if there else the default value.
  let inline getValueOr defaultValue = function Some v -> v | None -> defaultValue

module Array =
  let tryLast arr =
    let len = Array.length arr
    if len > 0 then Some arr.[len-1]
    else None

module List =
  /// Prepend element to list.
  let inline cons x xs = x::xs

type Mb<'a> = MailboxProcessor<'a>

/// Operations on unbounded FIFO mailboxes.
module Mb =

  /// Creates a new unbounded mailbox.
  let create () = Mb.Start (fun _ -> async.Return())

  /// Puts a message into a mailbox, no waiting.
  let inline put (a:'a) (mb:Mb<'a>) = mb.Post a

  /// Creates an async computation that completes when a message is available in a mailbox.
  let inline take (mb:Mb<'a>) = mb.Receive ()

type Async with
  static member inline bind (f:'a -> Async<'b>) (a:Async<'a>) : Async<'b> = async.Bind(a, f)

  /// Asynchronously await supplied task with the following variations:
  ///     *) Task cancellations are propagated as exceptions
  ///     *) Singleton AggregateExceptions are unwrapped and the offending exception passed to cancellation continuation
  static member inline AwaitTaskCorrect (task:Task<'a>) : Async<'a> =
    Async.FromContinuations <| fun (ok,err,cnc) ->
      task.ContinueWith (fun (t:Task<'a>) ->
        if t.IsFaulted then
            let e = t.Exception
            if e.InnerExceptions.Count = 1 then err e.InnerExceptions.[0]
            else err e
        elif t.IsCanceled then err (TaskCanceledException("Task wrapped with Async has been cancelled."))
        elif t.IsCompleted then ok t.Result
        else err(Exception "invalid Task state!")
      )
      |> ignore

  /// Like Async.StartWithContinuations but starts the computation on a ThreadPool thread.
  static member StartThreadPoolWithContinuations (a:Async<'a>, ok:'a -> unit, err:exn -> unit, cnc:OperationCanceledException -> unit, ?ct:CancellationToken) =
    let a = Async.SwitchToThreadPool () |> Async.bind (fun _ -> a)
    Async.StartWithContinuations (a, ok, err, cnc, defaultArg ct CancellationToken.None)

  /// Creates an async computation which completes when any of the argument computations completes.
  /// The other argument computation is cancelled.
  static member choose (a:Async<'a>) (b:Async<'a>) : Async<'a> =
    Async.FromContinuations <| fun (ok,err,cnc) ->
      let state = ref 0
      let cts = new CancellationTokenSource()
      let inline cancel () =
        cts.Cancel()
        cts.Dispose()
      let inline ok a =
        if (Interlocked.CompareExchange(state, 1, 0) = 0) then
          cancel ()
          ok a
      let inline err (ex:exn) =
        if (Interlocked.CompareExchange(state, 1, 0) = 0) then
          cancel ()
          err ex
      let inline cnc ex =
        if (Interlocked.CompareExchange(state, 1, 0) = 0) then
          cancel ()
          cnc ex
      Async.StartThreadPoolWithContinuations (a, ok, err, cnc, cts.Token)
      Async.StartThreadPoolWithContinuations (b, ok, err, cnc, cts.Token)

  static member internal chooseTasks2 (a:Task<'T>) (b:Task) : Async<Choice<'T * Task, Task<'T>>> =
    async {
        let ta = a :> Task
        let! i = Task.WhenAny( ta, b ) |> Async.AwaitTask
        if i = ta then return (Choice1Of2 (a.Result, b))
        elif i = b then return (Choice2Of2 (a))
        else return! failwith "unreachable" }

module AsyncSeq =
  let bufferByTimeFold (timeMs:int) (flatten:'State -> 'T -> 'State) (initialState: 'State) (source:AsyncSeq<'T>) : AsyncSeq<'State> = asyncSeq {
    if (timeMs < 1) then invalidArg "timeMs" "must be positive"
    let mutable curState = initialState
    use ie = source.GetEnumerator()
    let rec loop (next:Task<'T option> option, waitFor:Task option) = asyncSeq {
      let! next =
        match next with
        | Some n -> async.Return n
        | None -> ie.MoveNext () |> Async.StartChildAsTask
      let waitFor =
        match waitFor with
        | Some w -> w
        | None -> Task.Delay timeMs
      let! res = Async.chooseTasks2 next waitFor
      match res with
      | Choice1Of2 (Some a,waitFor) ->
        curState <- flatten curState a
        yield! loop (None,Some waitFor)
      // reached the end of seq
      | Choice1Of2 (None,_) ->
          yield curState
      // time has expired
      | Choice2Of2 next ->
        yield curState
        curState <- initialState
        yield! loop (Some next, None) }
    yield! loop (None, None) }

module AsyncExtensions =
  type Async with
    /// Returns an async computation which runs the argument computation but raises an exception if it doesn't complete
    /// by the specified timeout.
    static member timeoutAfter (timeout:TimeSpan) (c:Async<'a>) = async {
        let! r = Async.StartChild(c, (int)timeout.TotalMilliseconds)
        return! r }