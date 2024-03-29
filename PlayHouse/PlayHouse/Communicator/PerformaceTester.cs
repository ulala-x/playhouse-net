﻿using PlayHouse.Production;
using System.Diagnostics;
using PlayHouse.Utils;

namespace PlayHouse.Communicator
{
    
    class PerformanceTester
    {
        private bool _showQps;
        private string _from;
        private Stopwatch _stopWatch = new Stopwatch();
        private int _counter;
        private Timer _timer;
        private readonly LOG<MessageLoop> _log = new ();

        public PerformanceTester(bool showQps,  string from = "Server")
        {
            _showQps    = showQps;
            _from       = from;
            _timer      = new Timer((obj) => { Qps(); }, null, Timeout.Infinite, Timeout.Infinite); 
        }

        public void IncCounter()
        {
            if (_showQps)
            {
                Interlocked.Increment(ref _counter);
            }
        }

        public void Stop()
        {
            if (_showQps)
            {
                _timer.Change(Timeout.Infinite, Timeout.Infinite);
            }
        }

        public void Start()
        {
            if (_showQps)
            {
                _stopWatch.Start();
                _timer.Change(1000, 1000);
            }
        }

        private void Qps()
        {
            try
            {
                _stopWatch.Stop();
                int messageCount = Interlocked.Exchange(ref _counter, 0);
                long seconds = _stopWatch.ElapsedMilliseconds / 1000L;

                int qps = (messageCount == 0 || seconds == 0L) ? 0 : messageCount / (int)seconds;

                _log.Info(()=>$"{_from}, {messageCount}, qps: {qps}");
            }
            finally
            {
                _stopWatch.Reset();
                _stopWatch.Start();
            }
        }
    }

}
