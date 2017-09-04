open System.Net.Http
open System
open FSharp.Control.Tasks
open System.IO
open Client.TradingApi


[<EntryPoint>]
let main argv = 
    let BTC_XMR = "BTC_XMR"
    let keys = File.ReadAllLines(@"..\..\Keys.txt")
    let apiKey, secret = keys.[0], keys.[1]
    let tradingClient = Client.TradingApi.Client(apiKey, secret)
    let publicClient = Client.PublicApi.Client()
    //let result = tradingClient.TransferBalance("BTC", 0.0005m, Margin, Exchange).Result
    let result = tradingClient.Buy(BTC_XMR, 0.02300001m, 0.1m).Result.Result()
    let openOrder = tradingClient.ReturnOpenOrders(BTC_XMR).Result.Result()
    let tets = openOrder |> Seq.head
    printfn "%d amt %M" tets.orderNumber tets.amount
    let result = tradingClient.CancelOrder(result.orderNumber).Result
    printfn "%s" (result.Content.ReadAsStringAsync().Result)
    Console.ReadKey() |> ignore
    ignore (Console.ReadKey())
    0