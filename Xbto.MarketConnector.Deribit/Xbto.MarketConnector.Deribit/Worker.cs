using System;
using System.Collections.Concurrent;
using System.Threading;

namespace Xbto.MarketConnector.Deribit
{
    /* Super-basic worker. Dequeue and executes Action's */
    public class Worker : IAsyncControllable
    {
        internal readonly ConcurrentQueue<Action> _q = new ConcurrentQueue<Action>();
        internal readonly AutoResetEvent _er = new AutoResetEvent(false);
        internal readonly AsyncController _stopper;

        public int Count => _q.Count;

        public Worker(AsyncController stopper)
        {
            _stopper = stopper;
        }

        public void Enqueue(Action a)
        {
            _q.Enqueue(a);
            _er.Set();
        }

        public void RunSync()
        {
            Action a;
            while (!_stopper.StopRequested)
            {
                try
                {
                    while (!_stopper.StopRequested)
                    {
                        if (_q.TryDequeue(out a))
                            a();
                        else
                            _er.WaitOne(1000);
                    }
                }catch(Exception e)
                {
                    Console.WriteLine("Worker EXCEPT (but still alive) " + e);
                }
            }
        }

        public void Stop()
        {
            _er.Set();
        }

    }
}