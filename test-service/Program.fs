// Learn more about F# at http://fsharp.org
// See the 'F# Tutorial' project for more help.

open System
open Microsoft.Azure.Documents
open Sagan.ChangefeedProcessor


let handler (docs,range) = async {
  //printfn "Processed %d documents" (Array.length docs)
  return Array.length docs
}

let prog (length:int, changefeedPosition) = async {
  printfn "---Progress Tracker---processed %O documents---" length
  printfn "~~~~Changefeed Position~~~~\n%A\n~~~~" changefeedPosition
}

let endpoint : CosmosEndpoint = {
  uri = Uri "https://qa-incredibles-equinox.documents.azure.com:443/"
  authKey = "HNh9XsGeUyuoZpxTZy9r1DqHOG8UuGsXBxDwJqy5RV2CF2dmZLiUGSQT0RE3YpyZu3R6kYYjUXdqelNteNy5tQ=="
  databaseName = "qa-incredibles-equinox"
  collectionName = "incredibles"
}

let config : Config = {
  BatchSize = 100
  RetryDelay = TimeSpan.FromSeconds 1.
  ProgressInterval = TimeSpan.FromSeconds 30.
  StartingPosition = Beginning
  StoppingPosition = None
}

let merge (input:int*int) : int =
    let a,b = input
    a + b

[<EntryPoint>]
let main argv = 
    go endpoint config PartitionSelectors.allPartitions handler prog merge
    |> Async.RunSynchronously
    0 // return an integer exit code
