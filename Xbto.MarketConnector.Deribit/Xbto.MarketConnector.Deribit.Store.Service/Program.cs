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
            // probe the components (auto-test : 

            string user_url = DeribitInfo.deribit_url_test;
            int user_fetch_freq_ms = 60*60*1000; // refresh referential all hours, 
            int user_waittime_in_ms = 10000; // wait before retry in case something is wrong
            int user_maxTickers = 250; // max capacity of this component 
            int user_maxRequests = 5; // max ws request  in //
            int user_maxBufferSize = 10; // very small to see the effects
            int user_saveHeadAfterMs= 5000; // very small to see the effects
            int user_wait_time_before_flush_in_secs = 30; 


            Console.WriteLine("=== STARTING ===");

            AsyncController ctrler = new AsyncController();

            var instruFetcher = new InstrumentFetcher(user_url, user_fetch_freq_ms, user_waittime_in_ms, ctrler);

            var dataStore = new DataStore(ctrler, user_wait_time_before_flush_in_secs, user_maxBufferSize, user_saveHeadAfterMs);

            var only_btc = new[] { "BTC-PERPETUAL" };
            var marketDataFetcher = new MarketDataFetcher(user_url, ctrler, instruFetcher, dataStore, only_btc, user_maxTickers, user_maxRequests);// new[] { "BTC-PERPETUAL" });

            ctrler.TakeControl(dataStore);
            ctrler.TakeControl(marketDataFetcher);
            ctrler.TakeControl(instruFetcher);

            ctrler.StartAsync();

            // this service serve request clients
            //  RunAndForget(() => { HistoricalDataFetcher.Serve(InstrumentFetcher.deribit_url_test, 5000, 5000, ptr, stopper); });

            Console.WriteLine("hit a key to leave"); Console.ReadLine();
            
            ctrler.StopSync();

            Console.WriteLine("=== BYE ===");


        }
    }
}
