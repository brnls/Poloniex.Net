module Client
open System.Security.Cryptography
open System.Net.Http
open System
open System.Web
open System.Threading
open System.Threading.Tasks
open FSharp.Control.Tasks
open Newtonsoft.Json

let utf8 = System.Text.Encoding.UTF8
let urlEncodedContentType = "application/x-www-form-urlencoded"

let readMessage<'a> (msg : HttpResponseMessage) = task {
    let! content = msg.Content.ReadAsStringAsync()
    return JsonConvert.DeserializeObject<'a>(content)
    }

let sendMessageWithDelay (client:HttpClient) (semaphore: SemaphoreSlim) = 
    fun message -> task {
        do! unitTask <| semaphore.WaitAsync()
        try
            let! message = client.SendAsync(message)
            do! unitTask <| Task.Delay(200)
            return message
        finally
            semaphore.Release() |> ignore
    }

type ClientResult<'a> =
    |Ok of HttpResponseMessage * 'a
    |Error of HttpResponseMessage
        with 
            member x.Result() = 
                match x with
                |Ok (_, result) -> result
                |Error rep -> failwith (rep.Content.ReadAsStringAsync().Result)

module ClientResult = 
    let fromResponse<'a> (response: HttpResponseMessage) = task {
        if response.IsSuccessStatusCode then 
            let! result = readMessage<'a> response
            return Ok (response, result)
        else return Error response
        }

module PublicApi = 

    let constructClient() =
        let client = new HttpClient()
        client.BaseAddress <- Uri(@"https://poloniex.com/public")
        client

    let constructMessage (command:string) (parameters: (string * string) seq) =
        let sb = Text.StringBuilder()
        sb.Append(sprintf "?command=%s" (HttpUtility.UrlEncode(command))) |> ignore
        for (key,value) in parameters do
            sb.Append(sprintf "&%s=%s" (HttpUtility.UrlEncode(key)) (HttpUtility.UrlEncode(value))) |> ignore
        let queryParameters = sb.ToString()
        let message = new HttpRequestMessage(HttpMethod.Get, queryParameters)
        message

    type Ticker = {
        last: decimal
        lowestAsk:decimal
        highestBid:decimal
        percentChange:decimal
        baseVolume:decimal
        quoteVolume:decimal
    }

    type Tickers = {
        BTC_XMR: Ticker
        BTC_DASH: Ticker
    }

    type Client() =
        let sendMessage = 
            let publicClient = constructClient()
            sendMessageWithDelay publicClient (new SemaphoreSlim(1))
                
        let sendMessagePublic command parameters = 
            let message = constructMessage command parameters
            sendMessage message
        
        member x.GetTickers() = task {
            let! response = sendMessagePublic "returnTicker" []
            return! response |> ClientResult.fromResponse<Tickers>
        }

module TradingApi = 

    let constructMessageSender apiKey secret =
        let constructHttpClient (apiKey:string) =
            let client = new HttpClient()
            client.BaseAddress <- Uri(@"https://poloniex.com/tradingApi")
            client.DefaultRequestHeaders.Add("Key", apiKey)
            client

        let constructMessage (command:string) (parameters: (string * string) seq) (sign: string -> string) =
            let sb = Text.StringBuilder()
            sb.Append(sprintf "command=%s&nonce=%d" (HttpUtility.UrlEncode(command)) (DateTime.Now.Ticks)) |> ignore
            for (key,value) in parameters do
                sb.Append(sprintf "&%s=%s" (HttpUtility.UrlEncode(key)) (HttpUtility.UrlEncode(value))) |> ignore
            let postParameters = sb.ToString()
            let message = new HttpRequestMessage(HttpMethod.Post, "")
            message.Content <- new StringContent(postParameters, utf8, urlEncodedContentType)
            message.Headers.Add("Sign", sign postParameters)
            message

        let constructMessageSigner (secret:string) =
            let hasher = new HMACSHA512(utf8.GetBytes secret)
            fun (message:string) -> BitConverter.ToString(hasher.ComputeHash(utf8.GetBytes message)).Replace("-", "").ToLower()

        let client = constructHttpClient apiKey
        let messageSigner = constructMessageSigner secret
        let constructMessage command parameters = constructMessage command parameters messageSigner
        let semaphore = (new SemaphoreSlim(1))
        fun command parameters ->
            let message = constructMessage command parameters
            sendMessageWithDelay client semaphore message
        

    type Balances = {
        Xmr: decimal
    }

    type BalanceTypes = {
        available: decimal
        onOrders: decimal
        btcValue: decimal
    }

    type CompleteBalances = {
        Xmr: BalanceTypes
    }

    type Address = {
        Eth: string
    }

    type NewAddress = {
        success: int
        response: string
    }

    type AccountType =
    |Exchange
    |Margin
    |Lending

    type OpenOrder = {
        orderNumber: int64
        ``type``: string
        rate: decimal
        amount: decimal
        total: decimal
        }

    type TradeResult = {
        amount: decimal
        date: DateTimeOffset
        rate: decimal
        total: decimal
        tradeID: int64
        ``type``: string
        }

    type BuySellResponse = {
        orderNumber: int64
        resultingTrades: TradeResult seq
        }

    type ExchangeBalances = {
        exchange: Balances
        margin: Balances
        lending: Balances
    }

    type TradePair = {
        Btc: decimal
        Xmr: decimal
    }

    type TradeBalances = {
        Btc_Xmr: TradePair
    }

    type Client(apiKey:string, secret:string) =

        let sendMessage = constructMessageSender apiKey secret

        member x.ReturnBalances() = task {
            let! response = sendMessage "returnBalances" []
            return! response |> ClientResult.fromResponse<Balances>
            }

        
        member x.ReturnCompleteBalances() = task {
            let! response = sendMessage "returnCompleteBalances" []
            return! response |> ClientResult.fromResponse<CompleteBalances>
            }
        

        member x.ReturnDepositAddresses() = task {
            let! response = sendMessage "returnDepositAddresses" []
            return! response |> ClientResult.fromResponse<Address>
            }

        
        member x.GenerateNewAddress(currency: string) = task {
            let param = [
                ("currency", currency)
            ]
            let! response = sendMessage "generateNewAddress" param
            return! response |> ClientResult.fromResponse<NewAddress>
            }

        
        member x.ReturnOpenOrders(currencyPair: string) = task {
            let param = [
                ("currencyPair", currencyPair)
            ]
            let! response = sendMessage "returnOpenOrders" param
            return! response |> ClientResult.fromResponse<OpenOrder seq>
            }


        member x.Buy(currencyPair: string, rate: decimal, amount: decimal) = task {
            let param = [
                ("currencyPair", currencyPair)
                ("rate", rate.ToString())
                ("amount", amount.ToString())
            ]
            let! response = sendMessage "buy" param
            return! response |> ClientResult.fromResponse<BuySellResponse>
            }
        

        member x.Sell(currencyPair: string, rate: decimal, amount: decimal) = task {
            let param = [
                ("currencyPair", currencyPair)
                ("rate", rate.ToString())
                ("amount", amount.ToString())
            ]
            let! response = sendMessage "sell" param
            return! response |> ClientResult.fromResponse<BuySellResponse>
            }
       
       
        member x.CancelOrder(orderNumber: int64) = 
            let param = [
                ("orderNumber", orderNumber.ToString())
            ]
            sendMessage "cancelOrder" param
        

        member x.MoveOrder(orderNumber: int64, rate: decimal) =
            let param = [
                ("orderNumber", orderNumber.ToString())
                ("rate", rate.ToString())
            ]
            sendMessage "moveOrder" param


        member x.Withdraw(currency: string, amount: decimal, address: string) = 
            let param = [
                ("currency", currency)
                ("amount", amount.ToString())
                ("address", address)
            ]
            sendMessage "withdraw" param

        
        member x.ReturnAvailableAccountBalances() = task {
            let! response = sendMessage "returnAvailableAccountBalances" []
            return! response |> ClientResult.fromResponse<ExchangeBalances>
            }


        member x.ReturnTradableBalances() = task {
            let! response = sendMessage "returnTradableBalances" []
            return! response |> ClientResult.fromResponse<TradeBalances>
            }


        member x.TransferBalance(currency: string, amount: decimal, fromAccount: AccountType, toAccount: AccountType) = 
            let param = [
                ("currency", currency)
                ("amount", amount.ToString())
                ("fromAccount", fromAccount.ToString().ToLower())
                ("toAccount", toAccount.ToString().ToLower())
                ]
            sendMessage "transferBalance" param 
