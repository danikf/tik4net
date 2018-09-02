using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;

namespace tik4net.Api
{
    internal static class TagSequence
    {
        private static volatile int _tagCounter = 0;

        internal static int Next()
        {
            int tag = Interlocked.Increment(ref _tagCounter);

            return tag;
        }
    }
}
