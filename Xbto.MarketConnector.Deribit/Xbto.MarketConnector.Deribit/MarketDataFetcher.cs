using Newtonsoft.Json;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using WebSocketSharp;

namespace Xbto.MarketConnector.Deribit
{
    /* 
     * This class enqueues quote data per instrument and guarantee that at least 1 ws-request is getting data from deribit. 
     * When instrument referential changes, a new websocket is created.
     * 
     * With more time : study a WebSocket pool manager could be usefull to implement the deribit limits 
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
        // no lock for this one, only one thread we never access
        ImmutableDictionary<string, ByInstru> _instru;
        AsyncController _ctrler;
        InstrumentFetcher _fetcher;
        List<WebSocket> _ws;
        List<long> _price_counter = new List<long>();
        // this thread reads the _incomingData and redirects the object in the correct queue

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
                public int id=0;
            }
        }

        public MarketDataFetcher(string url, AsyncController ctrler, InstrumentFetcher fetcher, string[] wantedInstruments, int maxTickers = 50000, int maxRequests = 3)
        {
            _maxTickers= maxTickers;
            _maxRequests = maxRequests;
            _wanted = wantedInstruments == null ? new string[0] : wantedInstruments;
            _ctrler = ctrler;
            _fetcher = fetcher;
            _deribit_url = url;

            _fetcher.NewInstru += OnNewInstru;
            _fetcher.DelInstru += OnDelInstru;


        }

        public void RunSync()
        {
            Console.WriteLine("MarketDataFetcher: starting");
            QuoteDataResponse.DataWithInstru d;
            int periodOfDisplayInSeconds = 5;
            
            ByInstru bi;
            long ticked = 0, dispatched=0;
            DateTime lastsnap=DateTime.Now;
            while (!_ctrler.StopRequested)
            {
                var now = DateTime.Now;
                if ((lastsnap.AddSeconds(periodOfDisplayInSeconds) - now).Ticks<0)
                {
                    lastsnap = now;
                    Console.WriteLine($"MarketDataFetcher: STATS ticks waiting {_incomingData.Count}, done {ticked}, out {dispatched} |  " + string.Join(" ", _price_counter.Select((s,i)=>$"#{i}:{s}")));
                }

                if (_incomingData.TryDequeue(out d))
                {
                    ++ticked;
                    if (!_instru.TryGetValue(d.instrument_name, out bi))
                    {
                        continue;
                    }
                    bi.Add(d);
                    ++dispatched;
                }
                else
                {
                    _waitData.WaitOne(100); // check sometimes if we need to leave
                }

            }
            Console.WriteLine("MarketDataFetcher: leaving main thread");


        }

        // terminate the websocket so a new one can be create later
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
                        Console.WriteLine("MarketDataFetcher: closing ws");
                        ws.Close();
                    }
                }
                catch (Exception e)
                {
                    Console.WriteLine("MarketDataFetcher: EXCEPT while closing ws : " + e.ToString());

                }
            }
            _ws.Clear();
        }

        void SwitchFeeder(ByInstru[] instru)
        {
            long iid=-1;
            try
            {
                List<WebSocket> wss = new List<WebSocket>(_maxRequests);
                Console.WriteLine("MarketDataFetcher: sending quote request for " + instru.Length + " instrus");
                int step = instru.Length / _maxRequests + (instru.Length % _maxRequests == 0 ? 0 : 1);
                int iix = 0;
                List<long> price_counter = new List<long>();
                while (instru.Length != 0)
                {
                    price_counter.Add(0);
                    var this_ws = instru.Take(step).ToArray();
                    instru = instru.Skip(step).ToArray();
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
                                    Console.WriteLine($"MarketDataFetcher: NOT AN ACK :\n{e.RawData}");
                                    price_counter[ix] = -1;
                                }
                                else
                                {
                                    Console.WriteLine($"MarketDataFetcher: # {id}/{ix} ack received {e.RawData}");
                                    price_counter[ix] = 0;
                                }
                                return;

                            }
                            _incomingData.Enqueue(obj.@params.data);
                            if(price_counter[ix]++==0)
                            {
                                Console.WriteLine($"MarketDataFetcher: # {id}/{ix} first price received {e.RawData}");
                            }

                            _waitData.Set();
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine($"MarketDataFetcher: # {id}/{ix} except " + ex.ToString());
                        }
                    };
                    ws.OnError += (sender, e) =>
                    {
                        Console.WriteLine($"MarketDataFetcher: # {id}/{ix} error " + e.ToString());
                    };
                    ws.OnOpen += (sender, e) =>
                    {
                        Console.WriteLine($"MarketDataFetcher: # {id}/{ix} open");
                    };
                    ws.OnClose += (sendr, e) =>
                    {
                        Console.WriteLine($"MarketDataFetcher: # {id}/{ix} closed");
                    };

                    ws.Connect();

                    Console.WriteLine($"MarketDataFetcher: # {id}/{ix} msg " + strmsg);

                    ws.Send(strmsg);
                    Console.WriteLine($"MarketDataFetcher: # {id}/{ix} quote sent");
                }//while

                KillWs();

                _price_counter = price_counter;
                _ws = wss;
            }
            catch (Exception e)
            {
                Console.WriteLine($"MarketDataFetcher: # {iid} SwitchFeeder EXCEPT " + e.ToString());

            }

        }

        void OnNewInstru(object sender, InstrumentDef[] e)
        {
            var instru = _instru == null ? new Dictionary<string, ByInstru>() : new Dictionary<string, ByInstru>(_instru);
            foreach (var ee in e)
            {
                Console.Write("MarketDataFetcher: new instru " + ee.instrument_name + " ... ");
                if (!instru.ContainsKey(ee.instrument_name) && (_wanted.Length == 0 || _wanted.Contains(ee.instrument_name)))
                {
                    Console.WriteLine("added");
                    instru.Add(ee.instrument_name, new ByInstru(ee));
                    if (instru.Count >= _maxTickers)
                    {
                        Console.Write($"MarketDataFetcher: maximum {_maxTickers} reached");
                        break;
                    }
                }
                else
                {
                    Console.WriteLine("filtered");
                }
            }

            var bim = ImmutableDictionary.CreateBuilder<string, ByInstru>();
            bim.AddRange(instru.AsEnumerable());
            _instru = bim.ToImmutable();
            SwitchFeeder(instru.Values.ToArray());

        }
        private void OnDelInstru(object sender, InstrumentDef[] e)
        {
            var instru = new Dictionary<string, ByInstru>(_instru);
            foreach (var ee in e)
            {
                Console.WriteLine("MarketDataFetcher: instrument removed " + ee.instrument_name);
                instru.Remove(ee.instrument_name);
            }
            var bim = ImmutableDictionary.CreateBuilder<string, ByInstru>();
            bim.AddRange(instru.AsEnumerable());
            _instru = bim.ToImmutable();
            SwitchFeeder(instru.Values.ToArray());

        }

        public void Stop()
        {
            Console.WriteLine("MarketDataFetcher: stopping");

            _fetcher.NewInstru -= OnNewInstru;
            _fetcher.DelInstru -= OnDelInstru;

            _waitData.Set();

            KillWs();
            Console.WriteLine("MarketDataFetcher: stopped");

        }

    }
}

