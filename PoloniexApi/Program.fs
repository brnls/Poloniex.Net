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
    //let result = tradingClient.TransferBalance("BTC", 0.0005m, Margin, Exchange).Result
    //let result = tradingClient.Buy("BTC_XMR", 0.02500001m, 0.1m).Result
    let result = tradingClient.CancelOrder(196735019163L).Result
    printfn "%s" (result.Content.ReadAsStringAsync().Result)
    Console.ReadKey() |> ignore
    ignore (Console.ReadKey())
    0