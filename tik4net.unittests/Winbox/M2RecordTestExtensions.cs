using System;
using System.Collections.Generic;
using tik4net.Winbox;

namespace tik4net.unittests.Winbox
{
    /// <summary>Builds the hand-made <see cref="M2Record"/>s the decode tests feed the codec.</summary>
    internal static class M2RecordTestExtensions
    {
        internal static M2Record ToM2Record<T>(this IEnumerable<T> fields, Func<T, int> key, Func<T, M2Value> value)
        {
            var rec = new M2Record();
            foreach (var f in fields) rec[key(f)] = value(f);
            return rec;
        }
    }
}
