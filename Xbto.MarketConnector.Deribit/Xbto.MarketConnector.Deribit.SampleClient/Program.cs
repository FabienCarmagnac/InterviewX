using Newtonsoft.Json;
using System;
using System.Diagnostics;
using System.Threading;
using WebSocketSharp;

namespace Xbto.MarketConnector.Deribit.SampleClient
{
    class Program
    {
        static void Main(string[] args)
        {
            string url = "ws://127.0.0.1" + HistoricalDataFetcher.UrlPath;
            using (WebSocket ws = new WebSocket(url))
            using (AutoResetEvent er = new AutoResetEvent(false))
            {
                //ws.Log.Level = LogLevel.Trace;
                //ws.Log.Output = (l, s) => { Console.WriteLine(l.ToString() + " | " + s); };

                HistoricalDataRequest request = new HistoricalDataRequest(){id = 0};
                long begin, end;
                request.@params.instrument_name = "BTC-PERPETUAL";
                DateTime u = DateTime.UtcNow;
                int offset_begin = int.Parse(args[0]);
                int offset_end = -int.Parse(args[0]);

                request.@params.begin_timestamp = begin = u.AddSeconds(offset_begin).ToDeribitTs();
                request.@params.end_timestamp = end =u.AddSeconds(offset_end).ToDeribitTs();

                Debug.Assert((end - begin) == (offset_end - offset_begin) * 1000);

                Console.WriteLine($"{DateTime.UtcNow.ToDeribitTs()} SampleClient: requesting begin={begin} end={end}");
                long uid = 0;

                QuoteData last=null;
                 
                ws.OnMessage += (sender, e) =>
                {

                    try
                    {
                        var raw = JsonConvert.DeserializeObject<HistoricalDataPayload>(e.Data);
                        if (raw.@params == null || raw.@params.quotes == null)
                        {
                            Console.WriteLine($"{DateTime.UtcNow.ToDeribitTs()} SampleClient: ack received");
                            return;
                        }

                        if (uid == 0)
                        {
                            Console.WriteLine($"{DateTime.UtcNow.ToDeribitTs()} SampleClient: getting first block of {raw.@params.quotes.Count} elements");
                        }

                        foreach (var t in raw.@params.quotes)
                        {
                            //Console.WriteLine($"{DateTime.UtcNow.ToDeribitTs()} {uid} {t.timestamp} [{t.best_bid_price} | {t.best_ask_price}]");

                            if(last!=null)
                            {
                                if (t.timestamp < last.timestamp)
                                {
                                    Console.WriteLine($"{DateTime.UtcNow.ToDeribitTs()} {uid} ERR {t.timestamp} older event [{t.best_bid_price} | {t.best_ask_price}]");
                                }
                                if (end < t.timestamp)
                                {
                                    Console.WriteLine($"{DateTime.UtcNow.ToDeribitTs()} {uid} ERR {t.timestamp} after end {end}");
                                }
                                if (t.timestamp < begin)
                                {
                                    Console.WriteLine($"{DateTime.UtcNow.ToDeribitTs()} {uid} ERR {t.timestamp} before begin {begin}");
                                }

                            }

                            last = t;

                            Console.WriteLine($"{DateTime.UtcNow.ToDeribitTs()} Latency={last.timestamp - DateTime.UtcNow.ToDeribitTs()}ms");

                            ++uid;
                        }

                        Console.WriteLine($"{DateTime.UtcNow.ToDeribitTs()} total received : {uid}");


                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"{DateTime.UtcNow.ToDeribitTs()} SampleClient: ws OnMessage EXCEPT: " + ex.ToString());
                    }
                };

                ws.OnError += (sender, e) =>
                {
                    Console.WriteLine("SampleClient: ws OnError: " + e.ToString());
                    er.Set();
                };
                ws.OnOpen += (sender, e) =>
                {
                    Console.WriteLine("SampleClient: ws open");
                };
                ws.OnClose += (sendr, e) =>
                {
                    Console.WriteLine("SampleClient: ws close");
                    er.Set();
                };

                ws.Connect();

                var cmd = JsonConvert.SerializeObject(request);
                Console.WriteLine("sending : " + cmd);

                ws.Send(cmd);

                er.WaitOne();

                Console.WriteLine("bye.");



            }


        }
    }
}
