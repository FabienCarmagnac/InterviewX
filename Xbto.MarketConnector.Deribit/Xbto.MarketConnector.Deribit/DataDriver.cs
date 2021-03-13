using System;
using System.Linq;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;

namespace Xbto.MarketConnector.Deribit
{
    public class DataDriver
    {
        readonly string _instruName;
        readonly ConcurrentQueue<Action> _proc;
        private QuoteData _last;

        public DataDriver(string instruName, ConcurrentQueue<Action> proc)
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
                    Console.WriteLine($"DataDriver: {_instruName} has no data");
                    // empty
                    return;
                }
                if(offset % QuoteData.SizeInBytes != 0)
                {
                    Console.WriteLine($"DataDriver: data corruped in {_instruName} (brutal stop ?), I will align the data for you");
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
                _last = QuoteData.FromStream(fic);
                Console.WriteLine($"DataDriver: {_instruName} has {(offset/ QuoteData.SizeInBytes)+1} elements in store, last ts={_last.timestamp}");

            }

        }

        public QuoteData GetLast()
        {
            return _last;
        }

        internal void SendToStore(List<QuoteData> timeData, InstruTimeSeries client)
        {
            var n = timeData == null ? 0 : timeData.Count;
          //  Console.WriteLine($"DataDriver: {_instruName} requested to save {n} quotes");
            _proc.Enqueue(() =>
            {
                {
                    if (timeData == null || timeData.Count == 0)
                    {
                        Console.WriteLine($"DataDriver: WARN {_instruName} nothing to save");
                        client.DataHaveBeenStoredTillIndex(0);
                        return;
                    }
                    using (var fic = File.Open(_instruName, FileMode.Append))
                    using (var memstr = new MemoryStream())
                    {
                        foreach (var q in timeData)
                        {
                            if (q.timestamp < _last.timestamp) // last one is older !
                                continue;
                            memstr.Write(q.Serialize());
                            _last = q;
                        }
                        fic.Write(memstr.ToArray());
                    }
                }
                client.DataHaveBeenStoredTillIndex(timeData.Count());
                //Console.WriteLine($"DataDriver: {_instruName} requested to save {n} quotes ==> DONE");

            });
            
        }
    }
}