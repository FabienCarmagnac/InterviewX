using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;

namespace Xbto.MarketConnector.Deribit
{
    internal class DataStore
    {
        //volatile bool _stop = false;
        //readonly CountdownEvent _stopper;
        //ConcurrentQueue<Action> _q;
        //DataStore(ConcurrentQueue<Action> q, CountdownEvent stopper)
        //{
        //    _stopper = stopper;
        //    _q = q;
        //    var t = new Thread(() => Run());
        //    t.IsBackground = true;
        //    t.Start();
        //}
        //internal void Stop()
        //{
        //    _stop = true;
        //}
        //internal void Run()
        //{
        //    Action a;
        //    while(!_stop)
        //    {
        //        if (_q.TryDequeue(out a)) a();
        //        else
        //            _er.Wait();
        //    }

        //}
    }
}