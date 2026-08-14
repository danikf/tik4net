using System;
using System.Collections.Generic;

namespace tik4net.Api
{
    /// <summary>
    /// Routes sentences read by <c>ApiConnection</c>'s single reader loop to the caller waiting for their
    /// tag, and releases every waiter at once when the connection ends (P2.3).
    /// </summary>
    /// <remarks>
    /// <para>
    /// Replaces the model where each caller took a read lock and pulled from the socket itself, shelving
    /// whatever belonged to someone else. That model did not withhold replies — the lock holder read for
    /// everyone — but it did make a caller's <c>ReceiveTimeout</c> depend on what else was in flight: a
    /// caller queued on the read lock had not started its deadline yet, so two silent commands failed at 1×
    /// and 2× the timeout and N of them at N× (F8). Here every caller waits on its own tag with its own
    /// deadline, because nobody has to become a reader to be answered.
    /// </para>
    /// <para>
    /// One monitor guards everything and <see cref="System.Threading.Monitor.PulseAll(object)"/> wakes
    /// waiters: a connection has a handful of concurrent commands, not thousands, and one lock that is never
    /// held across a socket operation is easier to reason about than per-tag events. Note the difference from
    /// the old lock — this one is only ever held for a dictionary operation, never while reading.
    /// </para>
    /// </remarks>
    internal sealed class ApiSentenceDispatcher
    {
        private const string UntaggedKey = "-empty-";

        private sealed class TagQueue
        {
            internal readonly Queue<ITikSentence> Items = new Queue<ITikSentence>();
            internal int Waiters;
        }

        private readonly object _sync = new object();
        private readonly Dictionary<string, TagQueue> _queues = new Dictionary<string, TagQueue>();

        // Non-null once the reader loop has ended: every present and future waiter gets this sentence
        // instead of blocking for a router that is not going to answer.
        private ITikSentence _termination;

        private static string KeyOf(string tag) => string.IsNullOrWhiteSpace(tag) ? UntaggedKey : tag;

        /// <summary>Hands a sentence to whoever is waiting for its tag (or queues it until they ask).</summary>
        internal void Push(ITikSentence sentence)
        {
            lock (_sync)
            {
                string key = KeyOf(sentence.Tag);
                if (!_queues.TryGetValue(key, out var queue))
                {
                    queue = new TagQueue();
                    _queues.Add(key, queue);
                }
                queue.Items.Enqueue(sentence);
                System.Threading.Monitor.PulseAll(_sync);
            }
        }

        /// <summary>
        /// Ends every wait, now and later, with <paramref name="sentence"/> — the synthetic <c>!fatal</c>
        /// built by the reader loop when the connection dies. Already-queued sentences are kept: a reply that
        /// arrived before the connection dropped is still that caller's answer.
        /// </summary>
        internal void TerminateAll(ITikSentence sentence)
        {
            lock (_sync)
            {
                _termination = sentence;
                System.Threading.Monitor.PulseAll(_sync);
            }
        }

        /// <summary>
        /// Waits for the next sentence carrying <paramref name="tag"/>.
        /// </summary>
        /// <param name="tag">Tag to wait for; empty for the untagged command.</param>
        /// <param name="timeoutMs">
        /// Caller's own deadline (<c>ReceiveTimeout</c>), or <c>0</c>/negative to wait indefinitely.
        /// </param>
        /// <exception cref="TikConnectionReceiveTimeoutException">
        /// Nothing arrived for this tag within the deadline. The connection is left alone: the sentence may
        /// still turn up, and it belongs to this tag's queue, not to whoever asks next.
        /// </exception>
        internal ITikSentence Wait(string tag, int timeoutMs)
        {
            string key = KeyOf(tag);
            var deadline = timeoutMs > 0
                ? (DateTime?)DateTime.UtcNow.AddMilliseconds(timeoutMs)
                : null;

            lock (_sync)
            {
                if (!_queues.TryGetValue(key, out var queue))
                {
                    queue = new TagQueue();
                    _queues.Add(key, queue);
                }

                queue.Waiters++;
                try
                {
                    while (queue.Items.Count == 0)
                    {
                        // A queued sentence outranks termination — see TerminateAll.
                        if (_termination != null)
                            return _termination;

                        if (deadline == null)
                        {
                            System.Threading.Monitor.Wait(_sync);
                            continue;
                        }

                        int remaining = (int)(deadline.Value - DateTime.UtcNow).TotalMilliseconds;
                        if (remaining <= 0 || !System.Threading.Monitor.Wait(_sync, remaining))
                        {
                            if (queue.Items.Count == 0 && _termination == null)
                                throw new TikConnectionReceiveTimeoutException(timeoutMs,
                                    $"No response received from the router within {timeoutMs} ms for "
                                    + (key == UntaggedKey ? "the untagged command." : $"tag '{tag}'."));
                        }
                    }

                    return queue.Items.Dequeue();
                }
                finally
                {
                    queue.Waiters--;
                    // Keep the queue alive while anyone is waiting on it: dropping it here would leave that
                    // waiter blocked on an object no pusher can find any more.
                    if (queue.Items.Count == 0 && queue.Waiters == 0)
                        _queues.Remove(key);
                }
            }
        }
    }
}
