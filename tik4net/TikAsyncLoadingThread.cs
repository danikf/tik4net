//using System;
//using System.Collections.Generic;
//using System.Linq;
//using System.Text;
//using System.Threading;

//namespace tik4net
//{
//    /// <summary>
//    /// Wrapper to ti async operation loading thread.
//    /// </summary>
//    public class TikAsyncLoadingThread
//    {
//        private readonly Thread _loadingThread;

//        internal TikAsyncLoadingThread(Action threadAction)
//        {
//            _loadingThread = new Thread(new ThreadStart(threadAction));
//            _loadingThread.IsBackground = true;
//        }

//        internal void Start()
//        {
//            _loadingThread.Start();
//        }

//        /// <summary>
//        /// Blocks the calling thread until a thread terminates, while continuing to perform
//        /// standard COM and SendMessage pumping.
//        /// </summary>
//        public void Join()
//        {
//            _loadingThread.Join();
//        }

//        /// <summary>
//        /// Blocks the calling thread until a thread terminates or the specified time elapses,
//        /// while continuing to perform standard COM and SendMessage pumping.
//        /// </summary>
//        public bool Join(int milisecondsTimeout)
//        {
//            return _loadingThread.Join(milisecondsTimeout);
//        }
//    }
//}
