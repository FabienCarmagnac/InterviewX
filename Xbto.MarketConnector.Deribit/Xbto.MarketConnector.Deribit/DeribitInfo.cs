using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;

namespace Xbto.MarketConnector.Deribit
{
    public static class DeribitInfo
    {
        public static string deribit_url_test = "wss://test.deribit.com/ws/api/v2";

        static long wsReqIndex = 0;

        public static long NextIndex => Interlocked.Increment(ref wsReqIndex);
    }
}
