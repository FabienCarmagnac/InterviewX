using System;
using System.Collections.Generic;

namespace Xbto.MarketConnector.Deribit
{
    public class ByInstru
    {
        readonly SortedDictionary<long, QuoteData> ByTime = new SortedDictionary<long, QuoteData>();

        public readonly InstrumentDef InstruDef;
        long _currentTs = long.MinValue;

        public ByInstru(InstrumentDef def)
        {
            this.InstruDef = def;
        }

        public void Add(QuoteData d)
        {
          //  Console.WriteLine($"MarketDataFetcher: quote {InstruDef.instrument_name} : [{d.best_bid_price} | {d.best_ask_price}]");

            if (_currentTs >= d.timestamp)
                return;

            ByTime.Add(_currentTs = d.timestamp, d);
        }

    }
}


