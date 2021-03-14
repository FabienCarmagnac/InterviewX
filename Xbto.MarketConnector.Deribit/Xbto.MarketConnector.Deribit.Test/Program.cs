using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;

namespace Xbto.MarketConnector.Deribit.Test
{
    class Program
    {
        static void Main(string[] args)
        {
            LLog.Info("=== BEGIN TESTS ===");
            TestDateTime();
            TestSerialDecimal();
            TestSerialQuoteData();
            TestInstruFetcher();
            TestDataStore();
            LLog.Info("=== END TESTS ===");
            LLog.Info("\n\nIf no ugly messages (eg stacktraces) have been displayed above, tests can be considered OK\n\n");
        }
        static void TestSerialDecimal()
        {
            LLog.Info("=== BEGIN TESTS TestSerialDecimal ===");

            decimal d = 123456789123456789;

            var d22 = new decimal(decimal.GetBits(d));
            Debug.Assert(d22 == d);


            byte[] b0 = d.SerialDecimal();

            decimal d2 = b0.ToDecimal();

            Debug.Assert(d2==d);
            LLog.Info("=== END TESTS TestSerialDecimal ===");

        }



        static void TestSerialQuoteData()
        {
            LLog.Info("=== BEGIN TESTS TestSerialQuoteData ===");
            decimal d = 123456789M;

            DateTime n = DateTime.UtcNow;
            var q = new QuoteData()
            {
                timestamp = n.ToDeribitTs(),
                best_ask_amount = d+1,
                best_ask_price = d+2,
                best_bid_amount = d+3,
                best_bid_price = d+4
            };

            var byt= q.BinSerialize();

            var q2 = new QuoteData(byt);

            Debug.Assert(q2.timestamp == q.timestamp, "timestamp");
            Debug.Assert(q2.best_bid_price == q.best_bid_price, "best_bid_price");
            Debug.Assert(q2.best_bid_amount == q.best_bid_amount, "best_bid_amount");
            Debug.Assert(q2.best_bid_price == q.best_bid_price, "best_bid_price");
            Debug.Assert(q2.best_bid_amount == q.best_bid_amount, "best_bid_amount");

            LLog.Info("=== END TESTS TestSerialQuoteData ===");
        
        }


        static void TestDateTime()
        {
            LLog.Info("=== BEGIN TESTS TestDateTime ===");
            long dt = 1615745488608;
            var d = dt.ToDateTime();
            Debug.Assert(d.Year == 2021);
            Debug.Assert(d.Month== 3);

            long dt2 = d.ToDeribitTs();
            Debug.Assert(dt==dt2);

            //

            long ll = DateTime.UtcNow.ToDeribitTs();


            LLog.Info("=== ENd TESTS TestDateTime ===");
        }

        static void TestInstruFetcher()
        {
            LLog.Info("=== BEGIN TESTS TestInstruFetcher ===");

            string user_url = DeribitInfo.deribit_url_test;
            int user_fetch_freq_ms = 100*1000;  // 100s
            int user_waittime_in_ms = 10000; // wait before retry in case something is wrong

            AsyncController ctrler = new AsyncController();

            var instruFetcher = new InstrumentFetcher(user_url, user_fetch_freq_ms, user_waittime_in_ms, ctrler);

            int size = -1;
            //Action<object, InstrumentDef[]> OnNewInstru = "

            instruFetcher.NewInstru += (e, i) =>
            {
                size=i.Length;
                ctrler.StopAsync();
            }; 

            ctrler.TakeControl(instruFetcher);
            ctrler.StartAsync();

            ctrler.WaitStop();

            Debug.Assert(size>10, "instrus");
            LLog.Info("=== END TESTS TestInstruFetcher ===");

        }

        static void TestDataStore()
        {
            LLog.Info("=== BEGIN TESTS TestDataStore ===");
            AsyncController ctrler = new AsyncController();

            var ds = new DataStore(ctrler, 1, 1, 2, 100000);

            ctrler.TakeControl(ds);
            ctrler.StartAsync();

            string instru = Guid.NewGuid().ToString();
            var st = ds.GetOrCreateInstruTimeSeries(new InstrumentDef() { instrument_name = instru });

            List<long> last_ts = new List<long>();
            for (int i = 0; i < 30; ++i)
            {
                var q = new QuoteData()
                {
                    timestamp =  DateTime.UtcNow.ToDeribitTs()
                };
                //LLog.Info(q.timestamp);
                last_ts.Add(q.timestamp);
                st.AddNextQuote(q);
                Thread.Sleep(100);
            }

            while (ds.QueueSize > 0)
                Thread.Sleep(100);

            ctrler.StopSync();

            DataDriver dd = new DataDriver(instru, null);

            var last = dd.GetLast();

            Debug.Assert(last != null, "last is null");
            Debug.Assert(last_ts.FindIndex(s => s==last.timestamp)>0 , "timestamp is not found");
            LLog.Info("=== END TESTS TestDataStore ===");


        }
    }
}
