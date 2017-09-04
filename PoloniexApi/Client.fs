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
            return response |> ClientResult.fromResponse<'a>
        }

module TradingApi = 
    open System.Security.Authentication

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

    type Balances = {
        Xmr: decimal
    }

    type AccountType =
    |Exchange
    |Margin
    |Lending

    type Client(apiKey:string, secret:string) =

        let sendMessage = 
            let client = constructHttpClient apiKey
            sendMessageWithDelay client (new SemaphoreSlim(1))
                
        let messageSigner = constructMessageSigner secret
        let constructMessage command parameters = constructMessage command parameters messageSigner

        let sendMessage command parameters = 
            let message = constructMessage command parameters
            sendMessage message

        member x.ReturnBalances() = task {
            let! response = sendMessage "returnBalances" []
            return! response |> ClientResult.fromResponse<Balances>
            }

        member x.TransferBalance(currency: string, amount:decimal, fromAccount: AccountType, toAccount :AccountType) = 
            let param = [
                ("currency", currency)
                ("amount", amount.ToString())
                ("fromAccount", fromAccount.ToString().ToLower())
                ("toAccount", toAccount.ToString().ToLower())
                ]
            sendMessage "transferBalance" param 
        
        member x.Withdraw(currency: string, amount:decimal, address: string) = 
            let param = [
                ("currency", currency)
                ("amount", amount.ToString())
                ("address", address)
            ]
            sendMessage "withdraw" param


