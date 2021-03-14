using Newtonsoft.Json;
using System;
using System.Diagnostics;
using System.Runtime.CompilerServices;
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
                //ws.LLog.Level = LogLevel.Trace;
                //ws.LLog.Output = (l, s) => { LLog.Info(l.ToString() + " | " + s); };

                HistoricalDataRequest request = new HistoricalDataRequest(){id = 0};
                long begin, end;
                DateTime u = DateTime.UtcNow;
                string symbol = args[0];
                int offset_begin = int.Parse(args[1]);
                int offset_end = int.Parse(args[2]);
                request.@params.instrument_name = symbol;

                request.@params.begin_timestamp = begin = u.AddSeconds(offset_begin).ToDeribitTs();
                request.@params.end_timestamp = end =u.AddSeconds(offset_end).ToDeribitTs();

                Debug.Assert((end - begin) == (offset_end - offset_begin) * 1000);

                LLog.Info($" SampleClient: requesting {symbol} begin={begin} end={end}");
                long uid = 0;

                QuoteData last=null;
                StrongBox<bool> is_rt = new StrongBox<bool>(false);
                ws.OnMessage += (sender, e) =>
                {

                    try
                    {
                        var raw = JsonConvert.DeserializeObject<HistoricalDataPayload>(e.Data);
                        if (raw.@params == null || raw.@params.quotes == null)
                        {
                            LLog.Info($" SampleClient: ack received");
                            return;
                        }

                        if (uid == 0)
                        {
                            LLog.Info($" SampleClient: getting first block of {raw.@params.quotes.Count} elements");
                        }

                        foreach (var t in raw.@params.quotes)
                        {
                            //LLog.Info($" {uid} {t.timestamp} [{t.best_bid_price} | {t.best_ask_price}]");

                            if(last!=null)
                            {
                                if (t.timestamp < last.timestamp)
                                {
                                    LLog.Info($" {uid} ERR {t.timestamp} older event [{t.best_bid_price} | {t.best_ask_price}]");
                                }
                                if (end < t.timestamp)
                                {
                                    LLog.Info($" {uid} ERR {t.timestamp} after end {end}");
                                }
                                if (t.timestamp < begin)
                                {
                                    LLog.Info($" {uid} ERR {t.timestamp} before begin {begin}");
                                }

                            }

                            last = t;

                            //LLog.Info($" Latency={last.timestamp - DateTime.UtcNow.ToDeribitTs()}ms");

                            if(last.timestamp - DateTime.UtcNow.ToDeribitTs()>0 && is_rt.Value==false)
                            {
                                LLog.Info($" I think we are RT now");
                                is_rt.Value = true;
                            }
                            ++uid;
                        }

                        LLog.Info($" total received : {uid}");


                    }
                    catch (Exception ex)
                    {
                        LLog.Info($" SampleClient: ws OnMessage EXCEPT: " + ex.ToString());
                    }
                };

                ws.OnError += (sender, e) =>
                {
                    LLog.Info("SampleClient: ws OnError: " + e.ToString());
                    er.Set();
                };
                ws.OnOpen += (sender, e) =>
                {
                    LLog.Info("SampleClient: ws open");
                };
                ws.OnClose += (sendr, e) =>
                {
                    LLog.Info("SampleClient: ws close");
                    er.Set();
                };

                ws.Connect();

                var cmd = JsonConvert.SerializeObject(request);
                LLog.Info("sending : " + cmd);

                ws.Send(cmd);

                er.WaitOne();

                LLog.Info("bye.");



            }


        }
    }
}
