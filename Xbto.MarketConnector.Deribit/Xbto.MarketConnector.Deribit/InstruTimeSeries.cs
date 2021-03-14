using System;
using System.Linq;
using System.Collections.Concurrent;
using System.Collections.Generic;

namespace Xbto.MarketConnector.Deribit
{
    public class InstruTimeSeries
    {
        readonly List<QuoteData> TimeData = new List<QuoteData>();

        public readonly InstrumentDef InstruDef;
        QuoteData _last;
        readonly DataDriver _dd;
        readonly DataStore _ds;
        bool _pendingStore;
        long _total = 0;

        public long Total => _total;
        public event Action<QuoteData> OnNewQuoteData;

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

            if (_ds.SaveHeadAfterMs < _last.FromNowInMs() ) // very illiquid product, save all
            {
                _pendingStore = true;
                _dd.SendToStore(TimeData.GetRange(0, TimeData.Count), this);
                return;
            }

        }
        public void GetSnapshot(long begin, long end, Action<List<QuoteData>> f)
        {

            lock (TimeData)
            {
                if (TimeData.Count == 0)
                    LLog.Info($"GetSnapshot: 0 elem");
                else
                    LLog.Info($"GetSnapshot: TimeData {TimeData[0]} => {TimeData.Last()}");

                int bx =-1, ex=-1;
                for(int i=0;i<TimeData.Count;++i)
                {
                    if (bx == -1 && TimeData[i].timestamp >= begin)
                        bx = i;
                    if (TimeData[i].timestamp > end)
                    {
                        ex = i;
                        break;
                    }
                }
                if (bx == -1)
                {
                    f(new List<QuoteData>());
                    return;
                }

                if (ex == -1) ex = TimeData.Count;
                
                f(TimeData.GetRange(bx, ex-bx));
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
                ++_total;
                InternalCheckIfNeedFlush();
            }
            OnNewQuoteData?.Invoke(d);
        }

        internal IEnumerable<QuoteData> GetHistoData(long begin, long end)
        {
            return _dd.GetHistoData(begin, end);
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


