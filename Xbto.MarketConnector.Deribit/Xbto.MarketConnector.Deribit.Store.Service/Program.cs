using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;

namespace Xbto.MarketConnector.Deribit.Store.Service
{

    /**
     * 
     * This program :
     *   - keep a referential of instrument update to date : InstrumentFetcher
     *   - store collected data for each instru of the referential : MarketDataFetcher
     * 
     * Each service manages its own thread and is functional aware of its role. See InstrumentFetcher and MarketDataFetcher
     * 
     * The scheduling is done by AsyncController (start, stop=join)
     * 
     * Once thee poke is done, interface
     */
    class Program
    {

        static void Main(string[] args)
        {
            #region quick drafts
            /*
                        HistoricalDataRequest hd = new HistoricalDataRequest();
                        hd.id = 152;
                        long n;
                        hd.@params.instrument_name = "BTC_PERPETUAL";
                        hd.@params.begin_timestamp = n=DateTime.UtcNow.AddDays(-1).ToDeribitTs();
                        hd.@params.end_timestamp = DateTime.UtcNow.AddSeconds(60).ToDeribitTs();

                        Console.WriteLine(JsonConvert.SerializeObject(hd, Formatting.Indented));


                        HistoricalDataRequestAck hdr = new HistoricalDataRequestAck();
                        hdr.id = 152;
                        hdr.@params.status_code = 1;

                        Console.WriteLine(JsonConvert.SerializeObject(hdr, Formatting.Indented));

                        HistoricalDataPayload dat = new HistoricalDataPayload();

                        QuoteData qd = new QuoteData();
                        qd.timestamp = n;
                        qd.best_ask_amount = 100.0M;
                        qd.best_bid_amount = 120.0M;
                        qd.best_ask_price = 124.9M;
                        qd.best_bid_price = 124.5M;

                        QuoteData qd2 = new QuoteData();
                        qd2.timestamp = n+2000000;
                        qd2.best_ask_amount = 90.0M;
                        qd2.best_bid_amount = 130.0M;
                        qd2.best_ask_price = 124.7M;
                        qd2.best_bid_price = 124.0M;

                        dat.@params.quotes.Add(qd);
                        dat.@params.quotes.Add(qd2);

                        Console.WriteLine(JsonConvert.SerializeObject(dat, Formatting.Indented));
            */
            #endregion


            string user_url = DeribitInfo.deribit_url_test;
            int user_fetch_freq_ms = 60*60*1000; // refresh referential all hours, 
            int user_waittime_in_ms = 10*1000; // wait before retry in case something is wrong
            int user_maxTickers = 5000; // max capacity of this component 
            int user_maxRequests = 10; // max ws request  in //
            int user_maxBufferSize = 100; // very small to see the effects
            int user_saveHeadAfterMs= 20000000; // very small to see the effects
            int user_wait_time_before_flush_in_secs = 5;
            int user_nb_io_thread=5;
            int user_max_nb_quotes_per_message=50;

            Console.WriteLine("=== STARTING ===");

            AsyncController ctrler = new AsyncController();

            var instruFetcher = new InstrumentFetcher(user_url, user_fetch_freq_ms, user_waittime_in_ms, ctrler);

            var dataStore = new DataStore(ctrler, user_nb_io_thread, user_wait_time_before_flush_in_secs, user_maxBufferSize, user_saveHeadAfterMs);

            string[] only_btc = new[] { "BTC-PERPETUAL" };
            var marketDataFetcher = new MarketDataFetcher(user_url, ctrler, instruFetcher, dataStore, only_btc, user_maxTickers, user_maxRequests);// new[] { "BTC-PERPETUAL" });

            var historicalFetcher = new HistoricalDataFetcher(ctrler, dataStore, user_max_nb_quotes_per_message);

            ctrler.TakeControl(dataStore);
            ctrler.TakeControl(marketDataFetcher);
            ctrler.TakeControl(instruFetcher);
            ctrler.TakeControl(historicalFetcher);

            ctrler.StartAsync();

            // this service serve request clients
            //  RunAndForget(() => { HistoricalDataFetcher.Serve(InstrumentFetcher.deribit_url_test, 5000, 5000, ptr, stopper); });

            Console.WriteLine("hit a key to leave"); Console.ReadLine();
            
            ctrler.StopSync();

            Console.WriteLine("=== BYE ===");


        }
    }
}
