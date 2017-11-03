using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Linq;
using System.Collections.Concurrent;

namespace InvertedTomato.TikLink {
    public class Result {
        /// <summary>
        /// Current state of the result.
        /// </summary>
        public bool IsDone { get; set; }
        public bool IsError { get; set; }

        /// <summary>
        /// Returned sentences.
        /// </summary>
        public ConcurrentBag<Sentence> Sentences { get; set; } = new ConcurrentBag<Sentence>();


        internal AutoResetEvent Block = new AutoResetEvent(false);

        /// <summary>
        /// Wait until the result is no longer pending.
        /// </summary>
        public Result Wait() {
            if (IsDone) {
                return this;
            }

            Block.WaitOne();
            return this;
        }

        /// <summary>
        /// Wait until the result is no longer pending with a given timeout
        /// </summary>
        public Result Wait(TimeSpan timeout) {
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
            if(!TryGetDoneAttribute(key, out var value)) {
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
            if(sentences.Count() != 1) {
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
    }
}
