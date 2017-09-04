open System.Net.Http
open System
open Newtonsoft.Json
open FSharp.Control.Tasks
open System.IO
open Client.TradingApi


[<EntryPoint>]
let main argv = 
    let tradingClient = Client.TradingApi.Client("api_key_here", "secret_here")
    let publicClient = Client.PublicApi.Client()
    let result = tradingClient.TransferBalance("XMR", 7.5M, Margin, Exchange).Result
    0