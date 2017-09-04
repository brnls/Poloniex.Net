open System.Net.Http
open System
open FSharp.Control.Tasks
open System.IO
open Client.TradingApi


[<EntryPoint>]
let main argv = 
    let keys = File.ReadAllLines(@"..\..\Keys.txt")
    let apiKey, secret = keys.[0], keys.[1]
    let tradingClient = Client.TradingApi.Client(apiKey, secret)
    let publicClient = Client.PublicApi.Client()
    let result = tradingClient.TransferBalance("BTC", 0.0005m, Exchange, Margin).Result
    0