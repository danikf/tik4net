using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;

namespace InvertedTomato.TikLink {
    public class CallResult {
        /// <summary>
        /// Is the call complete.
        /// </summary>
        public bool IsDone { get; set; }

        /// <summary>
        /// Did the call result in error.
        /// </summary>
        public bool IsError { get; set; }

        /// <summary>
        /// Returned sentences.
        /// </summary>
        public ConcurrentBag<Sentence> Sentences { get; set; } = new ConcurrentBag<Sentence>();

        private readonly string Tag;
        private readonly Link Link;
        internal readonly ManualResetEvent Block = new ManualResetEvent(false);

        public CallResult(Link link, string tag) {
            if (null == link) {
                throw new ArgumentException(nameof(link));
            }
            if (null == tag) {
                throw new ArgumentNullException(nameof(tag));
            }

            Link = link;
            Tag = tag;
        }

        /// <summary>
        /// Abort the request on the router.
        /// </summary>
        public void Cancel() { // TODO: Test
            var result = Link.Call("/cancel", new Dictionary<string, string>() {
                {"tag", Tag }
            }).Wait();

            if (result.IsError) {
                result.TryGetTrapAttribute("message", out var message);
                throw new CallException(message);
            }
        }

        /// <summary>
        /// Wait until the result is no longer pending.
        /// </summary>
        public CallResult Wait() {
            if (IsDone) {
                return this;
            }

            Block.WaitOne();
            return this;
        }

        /// <summary>
        /// Wait until the result is no longer pending with a given timeout
        /// </summary>
        public CallResult Wait(TimeSpan timeout) {
            if (IsDone) {
                return this;
            }

            Block.WaitOne(timeout);
            return this;
        }

        /// <summary>
        /// If there is one scalar sentence returned, get the attribute with the given key.
        /// </summary>
        public string GetDoneAttribute(string key) {
            if (!TryGetDoneAttribute(key, out var value)) {
                throw new KeyNotFoundException();
            }

            return value;
        }

        /// <summary>
        /// If there is one scalar sentence returned, get the attribute with the given key.
        /// </summary>
        public bool TryGetDoneAttribute(string key, out string value) {
            if (null == key) {
                throw new ArgumentNullException(nameof(key));
            }

            if (!IsDone) {
                throw new InvalidOperationException("Result not done.");
            }

            // Get single sentence
            var sentences = Sentences.Where(a => a.Command == "done");
            if (sentences.Count() != 1) {
                value = null;
                return false;
            }
            return sentences.Single().Attributes.TryGetValue(key, out value);
        }

        /// <summary>
        /// If there is one scalar sentence returned, get the attribute with the given key.
        /// </summary>
        public string GetTrapAttribute(string key) {
            if (!TryGetDoneAttribute(key, out var value)) {
                throw new KeyNotFoundException();
            }

            return value;
        }

        /// <summary>
        /// If there is one scalar sentence returned, get the attribute with the given key.
        /// </summary>
        public bool TryGetTrapAttribute(string key, out string value) {
            if (null == key) {
                throw new ArgumentNullException(nameof(key));
            }

            if (!IsDone) {
                throw new InvalidOperationException("Result not done.");
            }

            // Get single sentence
            var sentences = Sentences.Where(a => a.Command == "trap");
            if (sentences.Count() != 1) {
                value = null;
                return false;
            }
            return sentences.Single().Attributes.TryGetValue(key, out value);
        }

        public override string ToString() {
            var sb = new StringBuilder();
            if (IsDone) {
                sb.Append("done,");
            }
            if (IsError) {
                sb.Append("error,");
            }
            sb.Append(Sentences.Count);
            sb.Append(" sentences");

            return sb.ToString();
        }
    }
}
