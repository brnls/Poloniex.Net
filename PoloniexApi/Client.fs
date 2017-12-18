module Client
open System.Security.Cryptography
open System.Net.Http
open System
open System.Web
open System.Threading
open System.Threading.Tasks
open Newtonsoft.Json
open Giraffe

let utf8 = System.Text.Encoding.UTF8
let urlEncodedContentType = "application/x-www-form-urlencoded"
let jsonSerializerSettings = new JsonSerializerSettings(DateTimeZoneHandling = DateTimeZoneHandling.Utc)

let readMessage<'a> (msg : HttpResponseMessage) = task {
    let! content = msg.Content.ReadAsStringAsync()
    return JsonConvert.DeserializeObject<'a>(content, jsonSerializerSettings)
    }

let sendMessageWithDelay (client:HttpClient) (semaphore: SemaphoreSlim) = 
    fun message -> task {
        do! semaphore.WaitAsync()
        try
            let! message = client.SendAsync(message)
            do! Task.Delay(200)
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

    let constructMessageSender (apiKey:string) (secret:string) =
        let client = 
            let c = new HttpClient()
            c.BaseAddress <- Uri(@"https://poloniex.com/tradingApi")
            c.DefaultRequestHeaders.Add("Key", apiKey)
            c

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

        let messageSigner = 
            let hasher = new HMACSHA512(utf8.GetBytes secret)
            fun (message:string) -> BitConverter.ToString(hasher.ComputeHash(utf8.GetBytes message)).Replace("-", "").ToLower()

        fun command parameters ->
            let message = constructMessage command parameters messageSigner
            sendMessageWithDelay client (new SemaphoreSlim(1)) message
        

    type Balances = {
        XMR: decimal
    }

    type CompleteBalance = {
        available: decimal
        onOrders: decimal
        btcValue: decimal
    }

    type CompleteBalances = {
        XMR: CompleteBalance
    }

    type Address = {
        XMR: string
    }

    type NewAddress = {
        success: int
        response: string
    }

    type History = {
        currency: string
        address: string
        amount: decimal
        confirmations: int
        txid: string
        timestamp: int64
        status: string
    }

    type DepositWithdrawalHx = {
        deposits: History seq
        withdrawals: History seq
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

    type TradeHistory = {
        globalTradeID: int64
        tradeID: int64
        date: DateTime
        rate: decimal
        amount: decimal
        total: decimal
        fee: decimal
        orderNumber: int64
        ``type``: string
        category: string
    }

    type OrderTrades = {
        globalTradeID: int64
        tradeID: int64
        currencyPair: string
        ``type``: string
        rate: decimal
        amount: decimal
        total: decimal
        fee: decimal
        date: DateTime
    }

    type TradeResult = {
        amount: decimal
        date: DateTime
        rate: decimal
        total: decimal
        tradeID: int64
        ``type``: string
        }

    type BuySellResponse = {
        orderNumber: int64
        resultingTrades: TradeResult seq
        }

    type FeeInfo = {
        makerFee: decimal
        takerFee: decimal
        thirtyDayVolume: decimal
        nextTier: decimal
    }

    type ExchangeBalances = {
        exchange: Balances
        margin: Balances
        lending: Balances
    }

    type TradePair = {
        BTC: decimal
        XMR: decimal
    }

    type TradeBalances = {
        BTC_XMR: TradePair
    }

    type MarginSummary = {
        totalValue: decimal
        pl: decimal
        lendingFees: decimal
        netValue: decimal
        totalBorrowedValue: decimal
        currentMargin: decimal
    }

    type MarginOrder = {
        success: int
        message: string
        orderNumber: int64
        resultingTrades: TradeResult seq
    }

    type MarginPositionInfo = {
        amount: decimal
        total: decimal
        basePrice: decimal
        liquidationPrice: int
        pl: decimal
        lendingFees: decimal
        ``type``: string
    }

    type CloseMarginResult = {
        success: int
        message: string
        resultingTrades: TradeResult seq
    }

    type LoanOffer = {
        success: int
        message: string
        orderID: int64
        error: string
    }

    type OpenLoanOffer = {
        id: int64
        rate: decimal
        amount: decimal
        duration: int
        autoRenew: int
        date: DateTime
    }

    type OpenLoanResult = {
        XMR: OpenLoanOffer seq
        BTC: OpenLoanOffer seq
    }

    type ActiveLoan = {
        id: int64
        currency: string
        rate: decimal
        amount: decimal
        range: int
        autoRenew: int
        date: DateTime
        fees: decimal
    }

    type ActiveLoanResult = {
        provided: ActiveLoan seq
    }

    type LendingHistory = {
        id: int64
        currency: string
        rate: decimal
        amount: decimal
        duration: decimal
        interest: decimal
        fee: decimal
        earned: decimal
        ``open``: DateTime
        close: DateTime
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

        
        member x.ReturnDepositsWithdrawals(start: int64, ``end``: int64) = task {
            let param = [
                ("start", start.ToString())
                ("end", ``end``.ToString())
            ]
            let! response = sendMessage "returnDepositsWithdrawals" param
            return! response |> ClientResult.fromResponse<DepositWithdrawalHx>
            }

        
        member x.ReturnOpenOrders(currencyPair: string) = task {
            let param = [
                ("currencyPair", currencyPair)
            ]
            let! response = sendMessage "returnOpenOrders" param
            return! response |> ClientResult.fromResponse<OpenOrder seq>
            }

        
        member x.ReturnTradeHistory(currencyPair: string, start: int64, ``end``: int64, limit: int) = task {
            let param = [
                ("currencyPair", currencyPair)
                ("start", start.ToString())
                ("end", ``end``.ToString())
                ("limit", limit.ToString())
            ]
            let! response = sendMessage "returnTradeHistory" param
            return! response |> ClientResult.fromResponse<TradeHistory seq>
            }
        
        
        member x.ReturnOrderTrades(orderNumber: int64) = task {
            let param = [
                ("orderNumber", orderNumber.ToString())
            ]
            let! response = sendMessage "returnOrderTrades" param
            return! response |> ClientResult.fromResponse<OrderTrades seq>
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

        
        member x.ReturnFeeInfo() = task {
            let! response = sendMessage "returnFeeInfo" []
            return! response |> ClientResult.fromResponse<FeeInfo>
            }

        
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
        
        
        member x.ReturnMarginAccountSummary() = task {
            let! response = sendMessage "returnMarginAccountSummary" []
            return! response |> ClientResult.fromResponse<MarginSummary>
            }


        member x.MarginBuy(currencyPair: string, rate: decimal, amount: decimal) = task {
            let param = [
                ("currencyPair", currencyPair)
                ("rate", rate.ToString())
                ("amount", amount.ToString())
            ]
            let! response = sendMessage "marginBuy" param
            return! response |> ClientResult.fromResponse<MarginOrder>
            }


        member x.MarginSell(currencyPair: string, rate: decimal, amount: decimal) = task {
            let param = [
                ("currencyPair", currencyPair)
                ("rate", rate.ToString())
                ("amount", amount.ToString())
            ]
            let! response = sendMessage "marginSell" param
            return! response |> ClientResult.fromResponse<MarginOrder>
            }


        member x.GetMarginPosition(currencyPair: string) = task {
            let param = [
                ("currencyPair", currencyPair)
            ]
            let! response = sendMessage "getMarginPosition" param
            return! response |> ClientResult.fromResponse<MarginPositionInfo>
            }
        

        member x.CloseMarginPosition(currencyPair: string) = task {
            let param = [
                ("currencyPair", currencyPair)
            ]
            let! response = sendMessage "closeMarginPosition" param
            return! response |> ClientResult.fromResponse<CloseMarginResult>
            }

        
        member x.CreateLoanOffer(currency: string, amount: decimal, duration: int, autoRenew: int, lendingRate: decimal) = task {
            let param = [
                ("currency", currency)
                ("amount", amount.ToString())
                ("duration", duration.ToString())
                ("autoRenew", autoRenew.ToString())
                ("lendingRate", lendingRate.ToString())
            ]
            let! response = sendMessage "createLoanOffer" param
            return! response |> ClientResult.fromResponse<LoanOffer>
            }


        member x.CancelLoanOffer(orderNumber: int64) = 
            let param = [
                ("orderNumber", orderNumber.ToString())
            ]
            sendMessage "cancelLoanOffer" param


        member x.ReturnOpenLoanOffers() = task {
            let! response = sendMessage "returnOpenLoanOffers" []
            return! response |> ClientResult.fromResponse<OpenLoanResult>
            }


        member x.ReturnActiveLoans() = task {
            let! response = sendMessage "returnActiveLoans" []
            return! response |> ClientResult.fromResponse<ActiveLoanResult>
            }


        member x.ReturnLendingHistory(start: int64, ``end``: int64, limit: int) = task {
            let param = [
                ("start", start.ToString())
                ("end", ``end``.ToString())
                ("limit", limit.ToString())
            ]
            let! response = sendMessage "returnLendingHistory" param
            return! response |> ClientResult.fromResponse<LendingHistory seq>
            }

        
        member x.ToggleAutoRenew(orderNumber: int64) =
            let param = [
                ("orderNumber", orderNumber.ToString())
            ]
            sendMessage "toggleAutoRenew" param