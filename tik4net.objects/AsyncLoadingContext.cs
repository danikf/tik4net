//using System;
//using System.Collections.Generic;
//using System.Linq;
//using System.Text;

//namespace tik4net.Objects
//{
//    /// <summary>
//    /// Context of running asynchronous operation on mikrotik router.
//    /// </summary>
//    public class AsyncLoadingContext
//    {
//        private readonly ITikCommand _loadingCommand;
//        private readonly TikAsyncLoadingThread _loadingThread;

//        /// <summary>
//        /// Is true if operation is already running.
//        /// </summary>
//        public bool IsRunning
//        {
//            get { return _loadingCommand.IsRunning; }
//        }

//        internal AsyncLoadingContext(ITikCommand loadingCommand, TikAsyncLoadingThread loadingThread)
//        {
//            Guard.ArgumentNotNull(loadingCommand, "loadingCommand");
//            Guard.ArgumentNotNull(loadingThread, "loadingThread");

//            _loadingCommand = loadingCommand;
//            _loadingThread = loadingThread;
//        }

//        /// <summary>
//        /// Tries to cancel asynchronous operation.
//        /// </summary>
//        public void Cancel()
//        {
//            _loadingCommand.Cancel();
//        }

//        /// <summary>
//        /// Tries to cancel asynchronous operation and joins loading thread.
//        /// </summary>
//        /// <param name="milisecondsTimeout">Thread.Join timeout or 0 for unlimited timeout.</param>
//        public void CancelAndJoin(int milisecondsTimeout = 0)
//        {
//            _loadingCommand.Cancel();
//            if (_loadingCommand.IsRunning)
//            {
//                if (milisecondsTimeout <= 0)
//                    _loadingThread.Join();
//                else
//                    _loadingThread.Join(milisecondsTimeout);
//            }
//        }
//    }
//}

