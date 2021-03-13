using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;

namespace Xbto.MarketConnector.Deribit
{
    /*
     * Stores data in files.
     * The RunSync dequeue the IO request and periodically asks the InstruTimeSeries to check if something should be saved.
     * 
     * This class centralizes the config of the datadrivers:
     *      - maxBufferSize : size max of a memory buffer before requesting a flush to file
     *      - saveHeadAfterMs : if a product does not tick during this time, the full buffer is flushed.
     */
    public class DataStore : IAsyncControllable
    {
        AutoResetEvent _er = new AutoResetEvent(false);
        AsyncController _stopper;
        ConcurrentQueue<Action> _proc = new ConcurrentQueue<Action>();
        List<InstruTimeSeries> _tis = new List<InstruTimeSeries>();

        public readonly int MaxBufferSize;
        public readonly int SaveHeadAfterMs;

        private int _wait_time_before_flush_in_secs;

        public DataStore(AsyncController stopper, int wait_time_before_flush_in_secs, int maxBufferSize, int saveHeadAfterMs)
        {
            _stopper = stopper;
            MaxBufferSize  = maxBufferSize;
            SaveHeadAfterMs = saveHeadAfterMs;
            _wait_time_before_flush_in_secs = wait_time_before_flush_in_secs;
        }

        public int QueueSize => _proc.Count;

        public void RunSync()
        {
            Action a;
            DateTime next_check = DateTime.Now.AddSeconds(_wait_time_before_flush_in_secs);
            while (!_stopper.StopRequested) 
            {
                if (_proc.TryDequeue(out a))
                    a();
                else
                    _er.WaitOne(200);

                if(next_check < DateTime.Now) // time to check if 
                {
                    var t = _tis;
                    foreach(var tis in t)
                    {
                        tis.CheckIfNeedFlush();
                    }
                    next_check = DateTime.Now.AddSeconds(_wait_time_before_flush_in_secs);
                }
            } 
        }

        public void Stop()
        {
            _er.Set();
        }

        public  InstruTimeSeries GetOrCreateInstruTimeSeries(InstrumentDef ee)
        {
            InstruTimeSeries ret;
            lock (_tis)
            {
                ret = _tis.Find(s => s.InstruDef.instrument_name == ee.instrument_name);
                if (ret == null) // 
                    _tis.Add(ret=new InstruTimeSeries(ee, new DataDriver(ee.instrument_name, _proc), this));                
            }
            return ret;

        }
    }
}