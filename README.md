# InterviewX

# Objective 

Record market data coming from D. broker.

# Pinciples

The quotes are collected with several parallel webrequests.
All the deserialized data is added to a single queue which are consumed by a unique thread.
This consumer thread - only, and that's why it is unique - routes the data to a per-instrument queue.
When a queue seems ready to be flushed (several crits), the IO thread handling this instrument removes - all or part of - the in-memory data after it got the confirmation the data has been written to the per-instrument file.

The data is stored as binary-fixed-size object in file (see TODO).

# How to build

  * Open sln in visual studio  (2019 here)
  * Restore nuggets
  * Build, AnyCpu only.

# How to test

> Xbto.MarketConnector.Deribit.Test.exe

Warning: will request D. servers

# How to run the service :

> Xbto.MarketConnector.Deribit.Store.Service.exe OPTIONNAL_SYMBOL

  * The optional arg 0 : only one symbol wanted. If absent, all instruments of D. ref will be considered.

# How to run the client in DEBUG version : 

Ensure a server is running, then run :

> Xbto.MarketConnector.Deribit.SampleClient.exe BTC-PERPETUAL -20 30 

  * arg 0 : BTC-PERPETUAL : symbol
  * arg 1 : begin date, in relative seconds from 'now'
  * arg 2 : end  date, in relative seconds from 'now'

 
# Limitations & known bugs

  * If the program  is stopped and restarted, the program cant request the D. servers to fill the market data missing during down-time. The D. API does not support it.
  * There is no control about disk space.
  * The IO thread assignement per instrument does not check if instruments are related. This can lead to functionnal un-desirable behaviour like processing a price change for an instrument AFTER processing price change for options on this main instrument. Happily, the arch can allow this change very easily. See DataStore.cs:78

# Config

See service program:

 * int usr_fetch_freq_ms = 60*60*1000; // refresh referential period in ms
 * int user_waittime_in_ms = 10*1000; //referential wait time before retry after fatal error 
 * int user_maxTickers = 5000; // max number of tickers this component can handle
 * int user_maxRequests = 10; // max ws request in //
 * int user_maxBufferSize = 5; // maximum buffer size per instrument. When reached, data can still be enqueued but a storage request is sent.
 * int user_saveHeadAfterMs= 2000; // maximum wait time before deciding to force store a queue when it does not tick that much
 * int user_wait_time_before_flush_in_secs = 5; // in seconds : 
 * int user_nb_io_thread=5; // number of threads which read/write quote data files
 * int user_max_nb_quotes_per_message=50; // number of quote data in 1 block when replying to the client.
            
# Thanks to

  * https://github.com/sta/websocket-sharp : used for websocket connectivity. 
  * https://json2csharp.com/ : used to convert quickly json samples to c# classes
  * NewtonSoft.JSon : nugget for de/serial

# TODO struct & perf
  * Compress market data : use tick data to compress the fat decimal serial. Usually, price/tick_size fits in a 32b int, sometimes 24.
  * Implement WebSocketMgr with strategies to handle providers request limits
  * Robustify AsyncController : use Interlocked to figure out the precise step of the start or stop workflows.
  * Add disk space control + hysteresis-based notifications when disk is almost full
  * Configure data store folder or even better : a file abstraction to handle several files per instrument (storage by day/month ?)
  * Speedup the search for the first QuoteData to be sent

# TODO tech

  * Expose config
  * Uses a real logger instead of LLog class
  * Adds try/catch for some critical blocks to avoid zombie threads.
  * Add interfaces to ease testing (almost  everywhere)

# API 

 * Url is /HistoricalData
 * To subscribe, send this :
 ```
{
  "jsonrpc": "2.0",
  "id": 152,
  "method": "public/get_historical_data",
  "params": {
    "instrument_name": "BTC_PERPETUAL",
    "begin_timestamp": 1615634726030296,
    "end_timestamp": 1615721186053261
  }
}
```
Then, the client should receive an ack message:
```
{
  "jsonrpc": "2.0",
  "id": 152,
  "method": "public/get_historical_data",
  "params": {
    "status_code": 0
  }
}
```
 * status_code=1 means instrument is unknown and can not be subscribed. Ws is closed after that, nothing is expected to be received after this message.
 * status_code=0 means instrument is known and can be subscribed.
 
 Following ack with status_code=0, the client should receive some data as following :
 ```
 {
  "jsonrpc": "2.0",
  "id": 0,
  "method": "public/get_historical_data",
  "params": {
    "quotes": [
      {
        "timestamp": 1615635467369,
        "best_bid_price": 124.5,
        "best_bid_amount": 120.0,
        "best_ask_price": 124.9,
        "best_ask_amount": 100.0
      },
      {
        "timestamp": 1615635469369,
        "best_bid_price": 124.0,
        "best_bid_amount": 130.0,
        "best_ask_price": 124.7,
        "best_ask_amount": 90.0
      }
    ]
  }
}
```
 
