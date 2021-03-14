using System;
using System.Linq;
using WebSocketSharp;
using Newtonsoft.Json;
using System.Threading;
using System.Runtime.CompilerServices;
using System.Collections.Generic;

namespace Xbto.MarketConnector.Deribit
{

    /* This class fetchs periodically the deribit referential data and notify created or removed instruments with events: DelInstru and NewInstru.
     * Config :
     *   - url : base deribit url 
     *   - fetch_freq_ms : wait time between 2 referential refresh try
     *   - waittime_in_ms: timeout before considering websocket dead
     */
    public class InstrumentFetcher : IAsyncControllable
    {
        string _url;
        int _fetch_freq_ms;
        int _waittime_in_ms;
        AsyncController _ctrler;

        public InstrumentFetcher(string url, int fetch_freq_ms, int waittime_in_ms, AsyncController ctrler)
        {
            _url = url;
            _fetch_freq_ms = fetch_freq_ms;
            _waittime_in_ms = waittime_in_ms;
            _ctrler = ctrler;
        }

        public event EventHandler<InstrumentDef[]> NewInstru;
        public event EventHandler<InstrumentDef[]> DelInstru;

        // may be these com classes should be internal ...
        public class InstruDefArg
        {
            public class Params
            {

                public Currency currency;
                public InstruKind kind;
                public bool expired;
            }
            public string jsonrpc = "2.0";
            public long id = 7617;
            public string method = "public/get_instruments";
            public Params paramss = new Params();

        }

        public class InstruDefResponse
        {

            public string jsonrpc = "2.0";
            public long id = 7617;
            public InstrumentDef[] result;

        }

        public void RunSync()
        {
            LLog.Info($" InstrumentFetcher: start");

            const string ProtocolRequestPattern = @"{ ""jsonrpc"": ""2.0"", ""id"": {{id}},  ""method"": ""public/get_instruments"",  ""params"": { ""currency"": ""{{currency}}""}}";
            string[] ProtocolVariableCur = Enum.GetNames(typeof(Currency));

            try
            {
                using (CountdownEvent er = new CountdownEvent(ProtocolVariableCur.Length))
                using (WebSocket ws = new WebSocket(_url))
                {

                    Dictionary<string, InstrumentDef> dico = new Dictionary<string, InstrumentDef>();
                    Dictionary<string, InstrumentDef> tmp = new Dictionary<string, InstrumentDef>();

                    ws.OnMessage += (sender, e) =>
                    {
                        try
                        {
                            var raw = JsonConvert.DeserializeObject<InstruDefResponse>(e.Data);
                            if (raw.result != null)
                            {
                                LLog.Info($" InstrumentFetcher: ws {raw.result.Length} instrus received");

                                foreach (var res in raw.result)
                                {
                                    tmp[res.instrument_name] = res;
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            LLog.Err("InstrumentFetcher: ws OnMessage EXCEPT: " + ex.ToString());
                        }finally
                        {
                            //Console.WriteLine("InstrumentFetcher: ws OnMessage ");
                            er.Signal();
                        }
                    };
                    ws.OnError += (sender, e) =>
                    {
                        LLog.Err("InstrumentFetcher: ws OnError: " + e.ToString());
                    };
                    ws.OnOpen += (sender, e) =>
                    {
                       // Console.WriteLine("InstrumentFetcher: ws open");
                    };
                    ws.OnClose += (sendr, e) =>
                    {
                       // Console.WriteLine("InstrumentFetcher: ws close");
                    };



                    do
                    {
                        ws.Connect();

                        foreach (var cur in ProtocolVariableCur)
                        {
                            var req = ProtocolRequestPattern
                                .Replace("{{currency}}", cur)
                                .Replace("{{id}}", DeribitInfo.NextIndex.ToString());

                            LLog.Debug("InstrumentFetcher: request sent : " + req);
                            ws.Send(req);
                        }

                            
                        if (!er.Wait(_waittime_in_ms))
                        {
                            LLog.Wng("InstrumentFetcher: timeout, will retry later");
                        }
                        else
                        {
                            // fetch done, compute delta

                            // new : tmp + dico - dico
                            var news = tmp.Union(dico).Except(dico).Select(s => s.Value).ToArray();

                            if (news.Length != 0)
                            {
                                LLog.Info($" InstrumentFetcher: {news.Length} instru found");
                                NewInstru?.Invoke(this, news);
                            }

                            // removed : tmp x dico - tmp
                            var dels = tmp.Intersect(dico).Except(tmp).Select(s => s.Value).ToArray();
                            if (dels.Length != 0)
                            {
                                LLog.Info($" InstrumentFetcher: {news.Length} instru removed");
                                DelInstru?.Invoke(this, dels);
                            }

                            dico = tmp;
                        }

                        // rearm
                        er.Reset(ProtocolVariableCur.Length);

                    } while (_ctrler.WaitAndContinue(_fetch_freq_ms));

                }
            }
            catch (Exception e)
            {
                LLog.Err("InstrumentFetcher: EXCEPT " + e.ToString());
            }
            LLog.Debug("InstrumentFetcher: leaving main thread");
        }

        public void Stop()
        {
            LLog.Info("InstrumentFetcher: stopping");
        }
    }
}

