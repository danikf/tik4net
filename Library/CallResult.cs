using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;

namespace InvertedTomato.TikLink {
    public class CallResult {
        /// <summary>
        /// Current state of the result.
        /// </summary>
        public bool IsDone { get; set; }
        public bool IsError { get; set; }
        public string Tag { get; private set; }

        /// <summary>
        /// Returned sentences.
        /// </summary>
        public ConcurrentBag<Sentence> Sentences { get; set; } = new ConcurrentBag<Sentence>();


        internal ManualResetEvent Block = new ManualResetEvent(false);

        public CallResult(string tag) {
            if (null == tag) {
                throw new ArgumentNullException(nameof(tag));
            }

            Tag = tag;
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
