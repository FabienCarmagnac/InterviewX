using System;
using System.Linq;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using System.Runtime.CompilerServices;
using System.Threading;

namespace Xbto.MarketConnector.Deribit
{
    /*
     * This class handles read/writes QuoteData for 1 file.
     * For 1 instrument, it is guaranteed that all IOs are done in the same thread.
     * 
     */
    public class DataDriver
    {
        readonly string _instruName;
        readonly Worker _proc;
        private QuoteData _last;

        public DataDriver(string instruName, Worker proc)
        {
            _proc = proc;
            _instruName=instruName;
            if(!File.Exists(_instruName))
            {
                File.Create(_instruName).Close();
            }
            using (var fic = File.OpenRead(_instruName))
            {
                var offset = fic.Seek(0, SeekOrigin.End);
                if (offset==0)
                {
                    
                    LLog.Info($"DataDriver: {_instruName} is a new instrument");
                    // empty
                    return;
                }
                if(offset % QuoteData.SizeInBytes != 0)
                {
                    LLog.Wng($"DataDriver: data corruped in {_instruName} (brutal stop ?), I will align the data for you");
                    offset = fic.Seek(-offset % QuoteData.SizeInBytes, SeekOrigin.End);
                    byte[] b = new byte[offset];
                    fic.Read(b, 0, (int)offset);
                    fic.Close();
                    File.WriteAllBytes(_instruName, b); // erase aligned data
                    if (offset != 0)
                    {
                        offset -= QuoteData.SizeInBytes;
                        int l = (int)offset;
                        _last = new QuoteData(b, ref l);
                    }
                    return;
                }

                offset = fic.Seek(-QuoteData.SizeInBytes, SeekOrigin.End); // if not empty and aligned on size, go backwards and read last one
                _last = QuoteData.FromBinStream(fic);
                LLog.Info($"DataDriver: {_instruName} has {(offset/ QuoteData.SizeInBytes)+1} elements in store, last ts={_last.timestamp}");

            }

        }

        public IEnumerable<QuoteData> GetHistoData(long begin, long end)
        {
            StrongBox<byte[]> s = new StrongBox<byte[]>();
            AutoResetEvent ev = new AutoResetEvent(false);
            LLog.Info($"DataDriver: {_instruName} reading full file ...");
            _proc.Enqueue(() =>
            {
                try
                {

                    s.Value = File.ReadAllBytes(_instruName); // TODO ugly, should stream
                }
                catch (Exception e)
                {
                    throw new Exception("while reading file " + _instruName, e);
                }
                finally
                {
                    ev.Set();
                }
            });

            
            if(!ev.WaitOne(10000))// 10s
            {
                LLog.Err($"DataDriver: {_instruName} read timeout");
                yield break;
            }

            // read and notify
            int offset = 0;
            int n = s.Value.Length;
            
            LLog.Info($"DataDriver: {_instruName} reading full file : got {n/QuoteData.SizeInBytes} elements");
            
            while (offset < n)
            {
                var qd = new QuoteData(s.Value, ref offset);
                if (qd.timestamp < begin)
                    continue;
                
                if (qd.timestamp > end)
                    yield break;
                yield return qd;
            }
            LLog.Info($"DataDriver: {_instruName} SENT");

            yield break;
        }
        public QuoteData GetLast()
        {
            return _last;
        }

        internal void SendToStore(List<QuoteData> timeData, InstruTimeSeries client)
        {
            var n = timeData == null ? 0 : timeData.Count;
          //  LLog.Info($"DataDriver: {_instruName} requested to save {n} quotes");
            _proc.Enqueue(() =>
            {
                if (timeData == null || timeData.Count == 0)
                {
                    LLog.Wng($"DataDriver: {_instruName} nothing to save");
                    client.DataHaveBeenStoredTillIndex(0);
                    return;
                }
                using (var fic = File.Open(_instruName, FileMode.Append))
                using (var memstr = new MemoryStream())
                {
                    foreach (var q in timeData)
                    {
                        if (_last != null && q.timestamp < _last.timestamp) // last one is older !
                            continue;
                        memstr.Write(q.BinSerialize());
                        _last = q;
                    }
                    fic.Write(memstr.ToArray());
                }
                client.DataHaveBeenStoredTillIndex(timeData.Count());
                //LLog.Info($"DataDriver: {_instruName} requested to save {n} quotes ==> DONE");

            });
            
        }
    }
}