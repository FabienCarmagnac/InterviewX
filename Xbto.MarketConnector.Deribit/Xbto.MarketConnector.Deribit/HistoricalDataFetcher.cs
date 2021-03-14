using Newtonsoft.Json;
using System;
using System.Linq;
using System.Collections.Generic;
using System.Threading;
using WebSocketSharp;
using WebSocketSharp.Server;
using System.Collections.Concurrent;
using System.Diagnostics;

namespace Xbto.MarketConnector.Deribit
{

    public class HistoricalDataDistributor : WebSocketBehavior
    {
        AutoResetEvent _er = new AutoResetEvent(false);
        long _stop = 0;
        HistoricalDataFetcher _root;

        public HistoricalDataDistributor()
        {
            IgnoreExtensions = true;
        }

        public void SetRoot(HistoricalDataFetcher root)
        {
            _root = root;
        }
        protected override void OnClose(CloseEventArgs e)
        {
            Interlocked.Exchange(ref _stop, 1);
            _er.Set();
        }

        protected override void OnMessage(MessageEventArgs e)
        {
            int status_code = 0;
            string msg = "OK";

            InstruTimeSeries iis=null;
            HistoricalDataRequest h = JsonConvert.DeserializeObject<HistoricalDataRequest>(e.Data);

            if (h == null)
            {
                status_code = 1;
                msg = $"HistoricalDataDistributor: cant understand your message {e.Data}";
            }
            else
            {
                iis = _root.DataStore.GetInstruTimeSeries(h.@params.instrument_name);
                if (iis == null)
                {
                    status_code = 2;
                    msg = "instrument " + h.@params.instrument_name + " not found";
                }
                else if (h.@params.begin_timestamp > h.@params.end_timestamp)
                {
                    status_code = 3;
                    msg = "timestamp are crossed";
                }
            }
            var par = h.@params;
            var instruname = par.instrument_name;
            // ack
            HistoricalDataRequestAck ack = new HistoricalDataRequestAck()
            {
                id = h.id,
                @params = new HistoricalDataRequestAck.Params() { status_code = status_code, error_message = msg}
            };

            // send anyway
            Console.WriteLine($"{DateTime.UtcNow.ToDeribitTs()} HistoricalDataDistributor {instruname} id={h.id} : acking client");
            var ackbin = JsonConvert.SerializeObject(ack);
            Send(ackbin);

            // if error  => bye !
            if (status_code !=0)
            {
                Console.WriteLine($"{DateTime.UtcNow.ToDeribitTs()} HistoricalDataDistributor {instruname} id={h.id} : closing ERROR " + msg);
                Close();
                return;
            }

            // real thing begins here

            long begin = par.begin_timestamp;
            long end = par.end_timestamp;

            Console.WriteLine($"{DateTime.UtcNow.ToDeribitTs()} HistoricalDataDistributor {instruname} id={h.id} : client request {begin} => {end}");

            // 1 : prepare the recording of what we could miss during the file access

            ConcurrentQueue<QuoteData> cq = new ConcurrentQueue<QuoteData>();
            Action<QuoteData> real_time_cb = q =>
            {
                if (q.timestamp < begin)
                    return; // not started yet !

                if (q.timestamp > end)
                {
                    Console.WriteLine($"{DateTime.UtcNow.ToDeribitTs()} HistoricalDataDistributor {instruname} id={h.id} : real time callback reached end");
                    Interlocked.Exchange(ref _stop, 1);
                }else
                    cq.Enqueue(q);
                
                _er.Set();

            };
           
            // recording new updates
            iis.OnNewQuoteData += real_time_cb;

            // get the snapshot of what is missing 
            List<QuoteData> buffer = new List<QuoteData>();
            iis.GetSnapshot(begin, end, l =>
            {
                buffer.InsertRange(0, l);
            });
            if (buffer.Count == 0)
            {
                Console.WriteLine($"{DateTime.UtcNow.ToDeribitTs()} HistoricalDataDistributor {instruname} id={h.id} : buffer empty");
            }
            else 
                Console.WriteLine($"{DateTime.UtcNow.ToDeribitTs()} HistoricalDataDistributor {instruname} id={h.id} : buffer gave {buffer.Count} elements [{buffer.First()} => {buffer.Last()}]");

            try
            {

                // send the file data. begin constraints is guaranteed
                long sent = 0;
                long last_ts = begin;
                long first = 0;

                SendQuoteData(h.id, 0, iis.GetHistoData(begin, end), out first, ref last_ts, ref sent); ///last_ts is 0 here because we know the data has been filtered                
                Console.WriteLine($"{DateTime.UtcNow.ToDeribitTs()} HistoricalDataDistributor {instruname} id={h.id} : histo data sent {sent}, {first} => {last_ts}");
                // now send the buffer : may be overlap with file 
                SendQuoteData(h.id, last_ts, buffer, out first, ref last_ts, ref sent);
                Console.WriteLine($"{DateTime.UtcNow.ToDeribitTs()} HistoricalDataDistributor {instruname} id={h.id} : buffer data reached {sent}, {first} => {last_ts}");

                // now send the RT container. can overlap !
                Console.WriteLine($"{DateTime.UtcNow.ToDeribitTs()} HistoricalDataDistributor {instruname} id={h.id} : RT begins");
                while (Interlocked.Read(ref _stop) == 0 && !_root.Stopper.StopRequested)
                {
                    QuoteData qd;
                    if (cq.TryDequeue(out qd))
                    {
                        if (qd.timestamp > end)
                        {
                            Console.WriteLine($"{DateTime.UtcNow.ToDeribitTs()} HistoricalDataDistributor {instruname} id={h.id} : RT end={end} < incoming={qd.timestamp}");
                            break;
                        }

                        var payload = new HistoricalDataPayload() { id = h.id };
                        payload.@params.quotes.Add(qd);
                        Send(JsonConvert.SerializeObject(payload));
                        ++sent;
                    }
                    else
                        _er.WaitOne(500);

                    var dts = DateTime.UtcNow.ToDeribitTs();
                    if (dts > end) // now has passed end time. Leave because it is possible the feed will never give you a message with that condition !
                    {
                        Console.WriteLine($"{DateTime.UtcNow.ToDeribitTs()} HistoricalDataDistributor {instruname} id={h.id} : RT end={end} < clock={dts}");
                        break;
                    }
                }
                Console.WriteLine($"{DateTime.UtcNow.ToDeribitTs()} HistoricalDataDistributor {instruname} id={h.id} : RT ends, total {sent}");

            }
            catch (Exception ex)
            {
                Console.WriteLine($"{DateTime.UtcNow.ToDeribitTs()} HistoricalDataDistributor {instruname} id={h.id} while sending : EXCEPT " + ex.ToString());
            }

            // unsubscribe
            iis.OnNewQuoteData -= real_time_cb;
            Close();

            Console.WriteLine($"{DateTime.UtcNow.ToDeribitTs()} HistoricalDataDistributor {instruname} id={h.id} DONE");


        }
        void SendQuoteData(long id, long last_ts, IEnumerable<QuoteData> qds, out long first_sent, ref long last_sent, ref long sent)
        {
            first_sent = -1;
            HistoricalDataPayload payload = null;
            foreach (var qd in qds)
            {
                if (last_sent > qd.timestamp)
                    continue;

                if (payload == null)
                {
                    payload = new HistoricalDataPayload() { id = id };
                    first_sent = qd.timestamp;
                }

                payload.@params.quotes.Add(qd);
                last_sent= qd.timestamp;
                ++sent;

                if (payload.@params.quotes.Count == _root.MaxNbQuotesPerMessage)
                {
                    Send(JsonConvert.SerializeObject(payload));
                    payload = new HistoricalDataPayload() { id = id };
                }
            }
            if (payload!=null && payload.@params.quotes.Count>0)
            {
                Send(JsonConvert.SerializeObject(payload));
            }
        }
    }

    #region boring com data
    public class HistoricalDataRequest
    {

        public class Params
        {
            public string instrument_name;
            public long begin_timestamp;
            public long end_timestamp;
        }

        public string jsonrpc = "2.0";
        public long id;
        public string method = "public/get_historical_data";
        public Params @params = new Params();
    }
        
        public class HistoricalDataRequestAck
        {
            public class Params
            {
                public int status_code;
                public string error_message;
            }

            public string jsonrpc = "2.0";
            public long id;
            public string method = "public/get_historical_data";
            public Params @params = new Params();
        }
    public class HistoricalDataPayload
    {
        public class Params
        {
            public List<QuoteData> quotes = new List<QuoteData>();
        }

        public string jsonrpc = "2.0";
        public long id;
        public string method = "public/get_historical_data";
        public Params @params = new Params();
    }

    #endregion boring com data

    public class HistoricalDataFetcher : IAsyncControllable
    {
        public const string UrlPath = "/HistoricalData";
        public readonly DataStore DataStore;
        public readonly int MaxNbQuotesPerMessage;
        public readonly AsyncController Stopper;
        public HistoricalDataFetcher(AsyncController stopper, DataStore dataStore, int maxNbQuotesPerMessage)
        {
            Stopper = stopper;
            DataStore = dataStore;
            MaxNbQuotesPerMessage = maxNbQuotesPerMessage;
        }
        public void RunSync()
        {
            var ws = new WebSocketServer("ws://127.0.0.1");
            ws.AddWebSocketService<HistoricalDataDistributor>(UrlPath, (h)=> 
            {
                Console.WriteLine("HistoricalDataFetcher: connecting client ...");
                h.SetRoot(this); 
            });

            Console.WriteLine("HistoricalDataFetcher: starting");
            ws.Start();
            while (Stopper.WaitAndContinue(5000)) ;
            ws.Stop();
            Console.WriteLine("HistoricalDataFetcher: stopping");

        }

        public void Stop()
        {

        }
    }
}