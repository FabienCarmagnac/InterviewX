using System;
using System.Collections.Concurrent;
using System.Collections.Generic;

namespace Xbto.MarketConnector.Deribit
{
    public class InstruTimeSeries
    {
        readonly List<QuoteData> TimeData = new List<QuoteData>();

        public readonly InstrumentDef InstruDef;
        QuoteData _last;
        DataDriver _dd;
        private readonly DataStore _ds;
        private bool _pendingStore;

        public InstruTimeSeries(InstrumentDef def, DataDriver dd, DataStore ds)
        {
            _dd = dd;
            _ds = ds;
            _last = _dd.GetLast();
            InstruDef = def;
        }

        public void CheckIfNeedFlush()
        {
            lock (TimeData)
            {
                InternalCheckIfNeedFlush();
            }
        }

        void InternalCheckIfNeedFlush()
        {
            if (_last == null || _pendingStore || TimeData.Count==0)
                return;

            if (_ds.MaxBufferSize < TimeData.Count)
            {
                _pendingStore = true;
                _dd.SendToStore(TimeData.GetRange(0, _ds.MaxBufferSize / 2), this);
                return;
            }

            if (_last.FromNowInMs() < _ds.SaveHeadAfterMs) // very illiquid product, save all
            {
                _pendingStore = true;
                _dd.SendToStore(TimeData.GetRange(0, TimeData.Count), this);
                return;
            }

        }
        // will be added only if the 
        public void AddNextQuote(QuoteData d)
        {
            //  Console.WriteLine($"MarketDataFetcher: quote {InstruDef.instrument_name} : [{d.best_bid_price} | {d.best_ask_price}]");

            // no need to lock here since _currentTs  is monotonic
            if (_last!=null && _last.timestamp >= d.timestamp)
                return;

            lock (TimeData)
            {
                TimeData.Add(d);
                _last = d;
                InternalCheckIfNeedFlush();
            }

        }

        public void DataHaveBeenStoredTillIndex(int lastObsoleteIndex)
        {

            lock (TimeData)
            {
                if(TimeData.Count< lastObsoleteIndex)
                {
                    Console.WriteLine($"DataHaveBeenStoredTillIndex WARN : buffer will be cleared, count={TimeData.Count}, index={lastObsoleteIndex}");
                    lastObsoleteIndex = TimeData.Count;
                }
                TimeData.RemoveRange(0, lastObsoleteIndex);
                _pendingStore = false;
            }
        }



    }
}


