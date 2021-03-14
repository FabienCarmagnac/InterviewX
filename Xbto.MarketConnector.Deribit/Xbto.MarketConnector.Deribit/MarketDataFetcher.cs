using Newtonsoft.Json;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using WebSocketSharp;

namespace Xbto.MarketConnector.Deribit
{
    /* 
     * This class requests data with several parallel ws, containing groups of instruments. 
     * The data is provided by the ws threads' and are concentrated in the unique _incomingData queue (@TODO should be a pool to increase throughput).
     * The RunSync thread dequeues _incomingData and pushes to the correct instrument queue. 
     *   
     * When instrument referential changes, the new whole of ws requests are runned, then the old ones are destroyed (so interruption of the stream).
     * The _incomingData queue is not supposed to be sorted by timestamp among all instruments data.
     * But it is sorted by timestamp for each instrument.
     * 
     * 
     * @SEE With more time : study a WebSocket pool manager could be usefull to implement the deribit limits 
     * see https://www.deribit.com/pages/information/rate-limits
     * 
     * 
     *    
     */
    public class MarketDataFetcher : IAsyncControllable
    {
        readonly AutoResetEvent _waitData = new AutoResetEvent(false);
        readonly ConcurrentQueue<QuoteDataResponse.DataWithInstru> _incomingData = new ConcurrentQueue<QuoteDataResponse.DataWithInstru>();
        readonly string _deribit_url;
        readonly string[] _wanted;
        int _maxTickers;
        int _maxRequests;
        DataStore _dataStore;
        // no lock for this one, content cant be changed
        ImmutableDictionary<string, InstruTimeSeries> _instru;
        // know when to stop
        AsyncController _ctrler;
        // instrument provider
        InstrumentFetcher _fetcher;
        // @TODO implement WebSocketMgr
        ImmutableList<WebSocket> _ws;
        // stats
        long[] _price_counter;
        // this thread 

        #region bad com objects
        public class QuoteDataResponse
        {
            public class Ack
            {
                public string jsonrpc;
                public long id { get; set; }
                public IList<string> result { get; set; }
                public long usIn { get; set; }
                public long usOut { get; set; }
                public long usDiff { get; set; }
                public bool testnet { get; set; }

            }
            public class DataWithInstru : QuoteData
            {
                public string instrument_name { get; set; }

                public QuoteData AsQuoteData()
                {
                    return new QuoteData()
                    {
                        timestamp = timestamp,
                        best_ask_amount = best_ask_amount,
                        best_ask_price = best_ask_price,
                        best_bid_amount = best_bid_amount,
                        best_bid_price = best_bid_price
                    };
                }
            }

            public class Params
            {
                public string channel { get; set; }
                public DataWithInstru data { get; set; }
            }

            public class Root
            {
                public string jsonrpc { get; set; }
                public string method { get; set; }
                public Params @params { get; set; }
            }

        }

        public class QuoteDataArg
        {

            public class Params
            {
                public List<string> channels = new List<string>();
            }

            public class Root
            {
                public string method = "public/subscribe";
                public Params @params = new Params();
                public string jsonrpc = "2.0";
                public long id=0;
            }
        }

        #endregion

        /*
         * when the ws are created, each of 'maxRequests' requests will carry min(nbtickers,maxTickers)/ maxRequests instruments         
         */
        public MarketDataFetcher(string url, AsyncController ctrler, InstrumentFetcher fetcher, DataStore dataStore, string[] wantedInstruments, int maxTickers = 50000, int maxRequests = 3)
        {
            _dataStore= dataStore;
            _maxTickers = maxTickers;
            _maxRequests = maxRequests;
            _wanted = wantedInstruments == null ? new string[0] : wantedInstruments;
            _ctrler = ctrler;
            _fetcher = fetcher;
            _deribit_url = url;

            _fetcher.NewInstru += OnNewInstru;
            _fetcher.DelInstru += OnDelInstru;


        }
        /*
         * Reads the _incomingData and redirects the object in the correct queue
         * */
        public void RunSync()
        {
            LLog.Info("MarketDataFetcher: starting");
            QuoteDataResponse.DataWithInstru d;
            int periodOfDisplayInSeconds = 5;
            
            InstruTimeSeries bi;
            long ticked = 0, dispatched=0;
            DateTime lastsnap=DateTime.UtcNow;
            while (!_ctrler.StopRequested)
            {
                var now = DateTime.UtcNow;
                if ((lastsnap.AddSeconds(periodOfDisplayInSeconds) - now).Ticks<0)
                {
                    lastsnap = now;
                    var pc = _price_counter;
                    LLog.Info($" MarketDataFetcher: STATS ticks waiting {_incomingData.Count}, done {ticked}, out {dispatched} |  " + string.Join(" ", pc.Select((s,i)=>$"#{i}:{s}")));

                    foreach(var ws in _ws)
                    {
                        if (ws.IsAlive) // avoid timeoout
                            ws.Ping();
                    }
                }

                if (_incomingData.TryDequeue(out d))
                {
                    ++ticked;
                    if (!_instru.TryGetValue(d.instrument_name, out bi))
                    {
                        continue;
                    }
                    if(bi.Total==0)
                    {
                        LLog.Info($" MarketDataFetcher: INSTRU {d.instrument_name} : first price [{d.best_bid_price} | {d.best_ask_price}]");
                    }
                    bi.AddNextQuote(d);
                    ++dispatched;
                }
                else
                {
                    _waitData.WaitOne(100); // check sometimes if we need to leave
                }

            }
            LLog.Info("MarketDataFetcher: leaving main thread");


        }

        // terminate the websockets so a new ones can be created later
        void KillWs()
        {
            if (_ws==null)
                return;

            foreach (var ws in _ws)
            {
                try
                {

                    if (ws != null && ws.ReadyState == WebSocketState.Open)
                    {
                        LLog.Info("MarketDataFetcher: closing ws");
                        ws.Close();
                    }
                }
                catch (Exception e)
                {
                    LLog.Info("MarketDataFetcher: EXCEPT while closing ws : " + e.ToString());

                }
            }
            _ws.Clear();
        }

        // reset all websockets
        void SwitchFeeder(InstruTimeSeries[] instru)
        {
            long iid=-1;
            try
            {
                var wss = ImmutableList.CreateBuilder<WebSocket>();
                LLog.Info("MarketDataFetcher: sending quote request for " + instru.Length + " instrus");
                int step = instru.Length / _maxRequests + (instru.Length % _maxRequests == 0 ? 0 : 1);
                int iix = 0;
                long[] price_counter = new long[_maxRequests];
                while (instru.Length != 0)
                {
                    var this_ws = instru.Take(step).ToArray();
                    instru = instru.Skip(step).ToArray();
                    
                    // use capture trick to inject data in the callbacks
                    int ix = iix++;

                    // prepare websocket
                    var ws = new WebSocket(_deribit_url);
                    wss.Add(ws);

                    // prepare message
                    var msg = new QuoteDataArg.Root();
                    msg.@params.channels.AddRange(this_ws.Select(s => "quote." + s.InstruDef.instrument_name));
                    long id = iid = DeribitInfo.NextIndex;
                    msg.id = Convert.ToInt32(id);


                    var strmsg = JsonConvert.SerializeObject(msg);
                    ws.OnMessage += (sender, e) =>
                    {
                        try
                        {
                            QuoteDataResponse.Root obj = JsonConvert.DeserializeObject<QuoteDataResponse.Root>(e.Data);
                            if (obj.@params == null)
                            {
                                QuoteDataResponse.Ack ack = JsonConvert.DeserializeObject<QuoteDataResponse.Ack>(e.Data);
                                if (ack == null || ack.result==null)
                                {
                                    LLog.Info($" MarketDataFetcher: ws {ix} NOT AN ACK :\n{e.Data}");
                                    price_counter[ix] = -1;
                                }
                                else
                                {
                                    LLog.Info($" MarketDataFetcher: # {id} ws {ix} ack received with {ack.result.Count} instrus confirmed");
                                    price_counter[ix] = 0;
                                }
                                return;

                            }
                            
                            //LLog.Info($" MarketDataFetcher: # quote ts={obj.@params.data.timestamp}");

                            _incomingData.Enqueue(obj.@params.data);
                            if(price_counter[ix]++==0)
                            {
                                LLog.Info($" MarketDataFetcher: # {id} ws {ix} first price received");
                            }

                            _waitData.Set();
                        }
                        catch (Exception ex)
                        {
                            LLog.Info($" MarketDataFetcher: # {id} ws {ix} except " + ex.ToString());
                        }
                    };
                    ws.OnError += (sender, e) =>
                    {
                        LLog.Info($" MarketDataFetcher: # {id} ws {ix} error " + e.ToString());
                    };
                    ws.OnOpen += (sender, e) =>
                    {
                        LLog.Info($" MarketDataFetcher: # {id} ws {ix} open");
                    };
                    ws.OnClose += (sendr, e) =>
                    {
                        LLog.Info($" MarketDataFetcher: # {id} ws {ix} closed");
                    };

                    ws.Connect();
                    //LLog.Info($" MarketDataFetcher: # {id} ws {ix} msg " + strmsg);

                    ws.Send(strmsg);
                    LLog.Info($" MarketDataFetcher: # {id} ws {ix} quote sent");
                }//while

                KillWs();

                _price_counter = price_counter;
                _ws = wss.ToImmutable();
            }
            catch (Exception e)
            {
                LLog.Info($" MarketDataFetcher: # {iid} SwitchFeeder EXCEPT " + e.ToString());

            }

        }
        /*
         * change the instru set and call SwitchFeeder 
         */
        void OnNewInstru(object sender, InstrumentDef[] e)
        {
            int filtered = 0;
            var instru0 = _instru;
            var instru = instru0 == null ? new Dictionary<string, InstruTimeSeries>() : new Dictionary<string, InstruTimeSeries>(instru0);
            foreach (var ee in e)
            {
              //  Console.Write("MarketDataFetcher: new instru " + ee.instrument_name + " ... ");
                if (!instru.ContainsKey(ee.instrument_name) && (_wanted.Length == 0 || _wanted.Contains(ee.instrument_name)))
                {
                //    LLog.Info("added");

                    instru.Add(ee.instrument_name, _dataStore.GetOrCreateInstruTimeSeries(ee));
                    if (instru.Count >= _maxTickers)
                    {
                        LLog.Info($" MarketDataFetcher: maximum {_maxTickers} reached");
                        break;
                    }
                }
                else
                {
                    //LLog.Info("filtered");
                    ++filtered;
                }
            }

            var bim = ImmutableDictionary.CreateBuilder<string, InstruTimeSeries>();
            bim.AddRange(instru.AsEnumerable());
            LLog.Info($"MarketDataFetcher: has {bim.Count} instruments, {filtered} filtered" );

            Interlocked.Exchange(ref _instru , bim.ToImmutable());
            SwitchFeeder(instru.Values.ToArray());

        }
        private void OnDelInstru(object sender, InstrumentDef[] e)
        {
            var instru = new Dictionary<string, InstruTimeSeries>(_instru);
            foreach (var ee in e)
            {
                LLog.Info("MarketDataFetcher: instrument removed " + ee.instrument_name);
                instru.Remove(ee.instrument_name);
            }
            var bim = ImmutableDictionary.CreateBuilder<string, InstruTimeSeries>();
            bim.AddRange(instru.AsEnumerable());
            Interlocked.Exchange(ref _instru, bim.ToImmutable());
            SwitchFeeder(instru.Values.ToArray());

        }

        public void Stop()
        {
            LLog.Info("MarketDataFetcher: stopping");

            _fetcher.NewInstru -= OnNewInstru;
            _fetcher.DelInstru -= OnDelInstru;

            _waitData.Set();

            KillWs();
            LLog.Info("MarketDataFetcher: stopped");

        }

    }
}

