using System;
using System.Collections.Concurrent;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace tik4net.Winbox
{
    /// <summary>
    /// Request/response multiplexer over an <see cref="IWinboxM2Channel"/>: one reader loop owns the read
    /// side and dispatches each inbound frame to the waiting caller by the echoed request id
    /// (<see cref="WinboxM2Protocol.SysKey.RequestId"/>, <c>0xFF0006</c>), so several M2 operations can be
    /// in flight at once instead of serializing on a single connection-wide lock.
    /// </summary>
    /// <remarks>
    /// <para><b>Why this is sound.</b> Three protocol facts, verified live and written up in
    /// <c>Docs/findings-winbox.md §12</c>:</para>
    /// <list type="number">
    ///   <item>The request id is echoed in the reply, so replies are attributable (§12.1). The
    ///     <c>0xFF0003</c> field is <i>not</i> a correlation key and must not be used (§12.2).</item>
    ///   <item>The frame crypto is stateless per frame — a fresh IV per frame, no cross-frame counter — so
    ///     frames may be decrypted and completed out of order (§12.3).</item>
    ///   <item>One request maps to exactly one reply frame; <c>0xFE0019</c> is an object count, not a
    ///     "more frames follow" marker, and getall pagination issues a fresh request per page (§12.7).</item>
    /// </list>
    /// <para><b>Scope.</b> Native M2 only, over either carrier — TCP since P2.1, the MAC layer since P2.42.
    /// The mepty terminal path (<c>WinboxCliClient</c>) streams unsolicited frames that carry no request id,
    /// so it keeps reading the channel directly and must never be given a multiplexer — see design §3.0. That
    /// is what <see cref="IWinboxM2Channel.SupportsReaderLoop"/> guards.</para>
    /// <para><b>Lifetime.</b> Start this only after connect, authentication and one-time init (router
    /// version, <c>.jg</c> catalog) have completed on the lockstep path: those sequences read the channel
    /// directly and would race the reader loop. Same reasoning as design §4.2.</para>
    /// </remarks>
    internal sealed class WinboxM2Multiplexer : IDisposable
    {
        private readonly IWinboxM2Channel _channel;
        private readonly ConcurrentDictionary<int, TaskCompletionSource<byte[]>> _pending
            = new ConcurrentDictionary<int, TaskCompletionSource<byte[]>>();
        private readonly object _writeLock = new object();
        private readonly Thread _readerThread;

        private int _reqId;
        private volatile bool _disposed;
        private volatile Exception? _fault;

        // What the reader loop has seen, for the timeout message. A timeout that names only the request id
        // cannot tell "the router is still working on this one" from "nothing has arrived on this channel at
        // all", and those want opposite fixes — the first is paging or a longer deadline, the second is a
        // dead channel. Written by the reader thread, read by whichever caller times out: int/long reads are
        // atomic on every platform this targets and an approximate count is exactly what the message needs.
        private long _framesRead;
        private long _lastFrameTicks;

        /// <summary>
        /// Invoked for an inbound frame that carries no request id, or one whose id has no pending
        /// registration (a reply that arrived after its caller timed out). Wired to the connection's trace
        /// hooks so discarded frames stay visible in diagnostics.
        /// </summary>
        internal Action<byte[]>? OnUnmatchedFrame { get; set; }

        internal WinboxM2Multiplexer(IWinboxM2Channel channel)
        {
            _channel = channel ?? throw new ArgumentNullException(nameof(channel));
            if (!channel.SupportsReaderLoop)
                throw new NotSupportedException(
                    $"{channel.GetType().Name} does not support a reader loop; check SupportsReaderLoop before constructing a multiplexer.");

            _readerThread = new Thread(ReaderLoop)
            {
                // A dedicated thread, not a pool thread: the loop blocks indefinitely on the socket for the
                // lifetime of the connection, which is precisely what the thread pool must not be used for.
                IsBackground = true,
                Name = "tik4net WinBox M2 reader",
            };
            _readerThread.Start();
        }

        /// <summary>
        /// Allocates the next request id and returns it as a system field, ready to be built into a message.
        /// </summary>
        /// <remarks>
        /// The id is one byte on the wire, so the counter wraps at 256. Id <c>0</c> is skipped and stays
        /// reserved for "no id" — <see cref="M2Message.ParseSysReqId"/> reports absence as <c>null</c>, and
        /// keeping 0 unused means a frame can never be mistaken for a reply to request 0. Reuse of an id
        /// that is still pending is refused at registration time in <see cref="SendReceive"/>.
        /// </remarks>
        internal byte[] NextReqIdField()
            => M2Message.U8Sys(WinboxM2Protocol.SysKey.RequestId, (byte)NextReqId());

        private int NextReqId()
        {
            int id;
            do { id = Interlocked.Increment(ref _reqId) & 0xFF; } while (id == 0);
            return id;
        }

        /// <summary>
        /// Sends one M2 message and waits for the reply carrying the same request id. The connection-wide
        /// lock is held only for the duration of the <i>write</i>, not the round-trip, so other callers can
        /// issue their requests while this one is still awaiting its reply.
        /// </summary>
        /// <param name="request">
        /// A built M2 message that must already contain a request id from <see cref="NextReqIdField"/>.
        /// </param>
        /// <param name="timeoutMs">
        /// Deadline for this request alone (design §4.2a). It has to survive a stalled router, not just a
        /// slow one: sustained load — from *any* connection, the ceiling is aggregate — clamps round trips
        /// from ~1 ms to ~20 ms, and on TCP that arrives as a batch of replies after seconds of silence.
        /// So a timeout here usually means the router went quiet, not that a reply was mislaid. See
        /// <c>Docs/findings-router-throughput-ceiling.md</c>.
        /// </param>
        internal byte[] SendReceive(byte[] request, int timeoutMs)
            => SendReceiveAsync(request, timeoutMs, CancellationToken.None).GetAwaiter().GetResult();

        /// <summary>
        /// The awaitable form of <see cref="SendReceive"/>, and the real implementation of both — the
        /// synchronous one blocks on this rather than the other way round (design D5: async is the primitive,
        /// nothing is pushed onto a thread-pool thread to look asynchronous).
        /// </summary>
        /// <param name="request"><inheritdoc cref="SendReceive" path="/param[@name='request']"/></param>
        /// <param name="timeoutMs"><inheritdoc cref="SendReceive" path="/param[@name='timeoutMs']"/></param>
        /// <param name="cancellationToken">
        /// Cancels the <i>wait</i>, not the router's work. M2 has no "abandon this request" verb for an
        /// ordinary round trip, so cancelling here drops the registration and lets the reply — if it ever
        /// comes — be discarded as unmatched. That is safe precisely because dispatch is by request id: a late
        /// reply is identified and dropped, never handed to whoever asked next. Streaming windows are the
        /// exception and do have a real router-side stop; see
        /// <see cref="WinboxNativeM2Operations.CancelMonitor"/>.
        /// </param>
        internal async Task<byte[]> SendReceiveAsync(byte[] request, int timeoutMs, CancellationToken cancellationToken)
        {
            ThrowIfFaulted();
            cancellationToken.ThrowIfCancellationRequested();

            int id = M2Message.ParseSysReqId(request)
                ?? throw new InvalidOperationException(
                    "A multiplexed M2 request must carry a request id (0xFF0006); build it with NextReqIdField().");

            var tcs = new TaskCompletionSource<byte[]>(TaskCreationOptions.RunContinuationsAsynchronously);

            // Register BEFORE the write: a fast reply can reach the reader loop before this method resumes,
            // and an unregistered id would be discarded as unmatched (design §4.3).
            if (!_pending.TryAdd(id, tcs))
                throw new InvalidOperationException(
                    $"M2 request id {id} is still awaiting a reply. With at most 256 ids in play this means " +
                    "requests are leaking registrations rather than legitimate concurrency.");

            try
            {
                // Serialize writes only. A frame is a sequence of chunks and two interleaved sends would
                // produce an unparseable stream. The send stays synchronous: a request is a handful of bytes
                // into the socket buffer, and neither channel offers an async send to await instead.
                lock (_writeLock)
                {
                    ThrowIfFaulted();
                    _channel.Send(request);
                }

                // The deadline and the token are raced against the reply rather than folded into a socket
                // timeout: the reader loop owns the read side with no per-read deadline of its own, precisely
                // so a timeout can never fire mid-frame and desynchronize the stream (design §4.2a).
                using (var giveUp = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
                {
                    var timeout = Task.Delay(timeoutMs, giveUp.Token);
                    var finished = await Task.WhenAny(tcs.Task, timeout).ConfigureAwait(false);
                    giveUp.Cancel();          // stop the timer whichever way this ended

                    if (finished != tcs.Task)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        throw new TimeoutException(
                            $"No WinBox M2 reply for request id {id} within {timeoutMs} ms. {ChannelActivity()}");
                    }
                }

                return await tcs.Task.ConfigureAwait(false);
            }
            finally
            {
                // Covers the timeout, cancellation and failure paths; on success the reader loop has already
                // removed it. Dropping the registration is what makes a late reply unmatched rather than
                // mistaken for the next request that reuses this id.
                _pending.TryRemove(id, out _);
            }
        }

        /// <summary>
        /// What the channel has been doing, for a timeout message. A silent channel and a slow answer look
        /// identical from the waiter's side, and they want opposite fixes.
        /// </summary>
        private string ChannelActivity()
        {
            long frames = Interlocked.Read(ref _framesRead);
            long ticks = Interlocked.Read(ref _lastFrameTicks);
            int waiters = _pending.Count;

            if (frames == 0)
                return $"No frame at all has arrived on this channel since it opened ({waiters} request(s) "
                     + "waiting) — the channel is silent, not slow.";

            string since = ticks == 0
                ? "unknown"
                : ((long)(DateTime.UtcNow - new DateTime(ticks, DateTimeKind.Utc)).TotalMilliseconds) + " ms ago";
            return $"The channel has read {frames} frame(s), the last {since}, with {waiters} request(s) "
                 + "waiting — so the channel is alive and this request is what is outstanding.";
        }

        private void ReaderLoop()
        {
            Exception? fault = null;
            try
            {
                while (!_disposed)
                {
                    byte[] m2 = _channel.ReceiveNextFrame();
                    if (m2 == null) break;                       // channel closed

                    _framesRead++;
                    Interlocked.Exchange(ref _lastFrameTicks, DateTime.UtcNow.Ticks);

                    int? id = M2Message.ParseSysReqId(m2);
                    if (id.HasValue && _pending.TryRemove(id.Value, out var tcs))
                        tcs.TrySetResult(m2);
                    else
                        OnUnmatchedFrame?.Invoke(m2);            // late reply or id-less frame — drop it
                }
            }
            catch (Exception ex)
            {
                fault = ex;
            }

            // Whatever ended the loop, no reply will ever arrive again: fail every waiter rather than
            // leaving callers to time out one by one.
            FaultAll(fault ?? new IOException("The WinBox M2 channel was closed."));
        }

        private void FaultAll(Exception ex)
        {
            _fault = ex;
            foreach (var key in _pending.Keys)
            {
                if (_pending.TryRemove(key, out var tcs))
                    tcs.TrySetException(ex);
            }
        }

        private void ThrowIfFaulted()
        {
            if (_disposed) throw new ObjectDisposedException(nameof(WinboxM2Multiplexer));
            var fault = _fault;
            if (fault != null)
                throw new IOException("The WinBox M2 channel is no longer usable.", fault);
        }

        /// <summary>
        /// Stops the reader loop and fails any outstanding request. Does <b>not</b> dispose the channel —
        /// the owning connection does that, and closing the channel is what unblocks the reader thread.
        /// </summary>
        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            FaultAll(new ObjectDisposedException(nameof(WinboxM2Multiplexer)));
        }
    }
}
