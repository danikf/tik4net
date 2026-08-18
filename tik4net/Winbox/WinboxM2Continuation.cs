using System.Collections.Generic;

namespace tik4net.Winbox
{
    /// <summary>
    /// The cursor a paginating handler returns on a <c>getall</c> (or a monitor query pass), carried back
    /// verbatim on the next request.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Two keys can carry it and a reply may set either or both:
    /// <see cref="WinboxM2Protocol.RecordKey.Continuation"/> (<c>ufe0003</c>, a u32) and
    /// <see cref="WinboxM2Protocol.RecordKey.ContinuationRaw"/> (<c>mfe0015</c>, a message-array). webfig
    /// echoes each one it received, unchanged, into the follow-up request:
    /// <code>
    /// else if ((rep.ufe0003 != null || rep.mfe0015) &amp;&amp; !me.block) {
    ///     if (rep.ufe0003 != null) req.ufe0003 = rep.ufe0003;
    ///     if (rep.mfe0015 != null) req.mfe0015 = rep.mfe0015;
    ///     post(req, onreply);
    /// }
    /// </code>
    /// </para>
    /// <para>
    /// This type holds the <b>raw TLV bytes</b> of whichever keys were present rather than decoded values.
    /// Neither token has any documented meaning — <c>0xFE0015</c> appears nowhere else in <c>master*.js</c>,
    /// so not even webfig knows what is inside one — and re-encoding a value from a decoded form is a guess
    /// about a shape we have never been told. Copying bytes is not a guess. It also removes a hazard the u32
    /// path carried: the token was round-tripped through <c>Convert.ToInt32</c>, which throws
    /// <see cref="System.OverflowException"/> for any cursor at or above <c>0x80000000</c>.
    /// </para>
    /// <para>See <c>Docs/findings-winbox.md</c> §12.7.1 for the source reading behind this.</para>
    /// </remarks>
    internal sealed class WinboxM2Continuation
    {
        private readonly List<byte[]> _fields;

        private WinboxM2Continuation(List<byte[]> fields)
        {
            _fields = fields;
        }

        /// <summary>
        /// Extracts the continuation cursor from <paramref name="reply"/>, or <c>null</c> when the reply
        /// carries neither continuation key — which is how a handler says "that was the last page".
        /// </summary>
        internal static WinboxM2Continuation? From(byte[] reply)
        {
            var fields = new List<byte[]>(2);
            AddIfPresent(fields, reply, WinboxM2Protocol.RecordKey.Continuation);
            AddIfPresent(fields, reply, WinboxM2Protocol.RecordKey.ContinuationRaw);
            return fields.Count == 0 ? null : new WinboxM2Continuation(fields);
        }

        private static void AddIfPresent(List<byte[]> fields, byte[] reply, int key)
        {
            byte[]? raw = M2Message.ExtractRawField(reply, key);
            if (raw != null) fields.Add(raw);
        }

        /// <summary>Appends the cursor's fields to a request under construction, exactly as received.</summary>
        internal void AppendTo(List<byte[]> head) => head.AddRange(_fields);
    }
}
