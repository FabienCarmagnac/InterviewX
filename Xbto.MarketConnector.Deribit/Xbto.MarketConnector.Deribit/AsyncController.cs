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
     * Start all services in they own threads
     * Stop trigger the wait handle + call a Stop for all services, then join their threads
     */
    public class AsyncController
    {
        long _stop = 0;
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

        public void StartAsync()
        {
            foreach(var l in _l)
            {
                var th = new Thread(() => l.RunSync()); 
                th.Start();
                _ths.Add(th);
            }
        }
        public void StopAsync()
        {
            Interlocked.Exchange(ref _stop, 1);
            _er.Set();

        }
        public void StopSync()
        {
            StopAsync();
            WaitStop();
        }
        public void WaitStop()
        { 
            foreach (var l in _l)
                l.Stop();

            foreach (var th in _ths)
                th.Join();
        }

        /* */
        public bool WaitAndContinue(int ms)
        {
            if (StopRequested)
                return false;
            return !_er.WaitOne(ms);
        }

        public bool StopRequested => Interlocked.Read(ref _stop) == 1;


    }

}
