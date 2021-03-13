using System;
using System.Linq;
using System.Collections.Generic;

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

        public readonly int MaxBufferSize;
        public readonly int SaveHeadAfterMs;

        readonly int _wait_time_before_flush_in_secs;
        readonly AsyncController _my_stopper = new AsyncController();
        readonly List<Worker> _proc = new List<Worker>();
        readonly List<InstruTimeSeries> _tis = new List<InstruTimeSeries>();

        readonly AsyncController _stopper;

        public DataStore(AsyncController stopper, int nbWriteThreads, int wait_time_before_flush_in_secs, int maxBufferSize, int saveHeadAfterMs)
        {
            _stopper = stopper;
            MaxBufferSize  = maxBufferSize;
            SaveHeadAfterMs = saveHeadAfterMs;
            _wait_time_before_flush_in_secs = wait_time_before_flush_in_secs;

            _proc = new List<Worker>(nbWriteThreads);
            for (int i=0;i< nbWriteThreads;++i)
            {
                Worker w = new Worker(_my_stopper);
                _my_stopper.TakeControl(w);
                _proc.Add(w);
            }
        }

        public int QueueSize => _proc.Sum(s => s.Count);

        public void RunSync()
        {
            _my_stopper.StartAsync();

            DateTime next_check = DateTime.Now.AddSeconds(_wait_time_before_flush_in_secs);
            while (!_stopper.StopRequested) 
            {
                if(next_check < DateTime.Now) // time to check if 
                {
                    var t = _tis;
                    foreach(var tis in t)
                    {
                        tis.CheckIfNeedFlush();
                    }
                    next_check = DateTime.Now.AddSeconds(_wait_time_before_flush_in_secs);
                }
                _stopper.WaitAndContinue(_wait_time_before_flush_in_secs);
            } 
        }

        public void Stop()
        {
            _my_stopper.StopSync();
        }

        public  InstruTimeSeries GetOrCreateInstruTimeSeries(InstrumentDef ee)
        {
            InstruTimeSeries ret;
            lock (_tis)
            {
                ret = _tis.Find(s => s.InstruDef.instrument_name == ee.instrument_name);
                if (ret == null) // 
                    _tis.Add(ret=new InstruTimeSeries(ee, new DataDriver(ee.instrument_name, _proc[_tis.Count%_proc.Count]), this));                
            }
            return ret;

        }
    }
}