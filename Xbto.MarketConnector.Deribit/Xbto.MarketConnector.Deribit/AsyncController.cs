using System.Collections.Generic;
using System.Threading;
using System.Linq;

namespace Xbto.MarketConnector.Deribit
{

    /* Basic interface to handle micro services */
    public interface IAsyncControllable
    {
        void RunSync(); // blocking
        void Stop(); // sync/join
    }


    /**
     * Start all services in their own thread.
     * Stop triggers the wait handle + call a Stop for all services, then join their threads.
     * 
     * No registration can be done during startup step.
     */
    public class AsyncController
    {
        long _stop =0;
        readonly AutoResetEvent _er = new AutoResetEvent(false);
        readonly List<IAsyncControllable> _l = new List<IAsyncControllable>();
        readonly List<Thread> _ths = new List<Thread>();
        public void TakeControl(IAsyncControllable s)
        {
            lock(_l)
            {
                _l.Add(s);
            }
        }

        /* Main thread should call this after all IAsyncControllable are under control. */
        public void StartAsync()
        {
            lock (_l)
            {
                foreach (var l in _l)
                {
                    var th = new Thread(() => l.RunSync());
                    th.Start();
                    _ths.Add(th);
                }
            }
        }
        /* StopAsync must be used in conjonction of WaitStop. */
        public void StopAsync()
        {
            Interlocked.Exchange(ref _stop, 1);
            _er.Set();

        }
        /* Stop everything in a blocking way. */
        public void StopSync()
        {
            StopAsync();
            WaitStop();
        }
        /* Must be called if the stop has been done with StopAsync */
        public void WaitStop()
        {
            lock (_l)
            {
                foreach (var l in _l)
                    l.Stop();

                foreach (var th in _ths)
                    th.Join();
            }
        }

        /* Typically used by services which must hold for some time but want to be notified when the stop has been requested.
         * Returns false if stopping (whatever the timeout occurs)
         */
        public bool WaitAndContinue(int ms)
        {
            if (StopRequested)
                return false;
            return !_er.WaitOne(ms);
        }

        /* Check if stopping */
        public bool StopRequested => Interlocked.Read(ref _stop) == 1;


    }

}
