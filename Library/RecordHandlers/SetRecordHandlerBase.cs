using InvertedTomato.TikLink.Records;
using System;
using System.Collections.Generic;
using System.Text;
using System.Linq;

namespace InvertedTomato.TikLink.RecordHandlers {
    public abstract class SetRecordHandlerBase<T> : FixedSetRecordHandlerBase<T> where T : SetRecordBase, new() {
        internal SetRecordHandlerBase(Link link) : base(link) { }

        /// <summary>
        /// Add a new record to a set of records.
        /// </summary>
        /// <remarks>
        /// While it's perfectly fine to use these methods directly, it's intended that you use the vanity methods instead (eg. link.Ip.Arp.Get()).
        /// </remarks>W
        /// <typeparam name="T"></typeparam>
        /// <param name="record">Record to be written</param>
        /// <param name="readBack">If TRUE, the record will be updated with a new copy of the record from the router</param>
        public virtual void Add(T record, bool readBack = false) {
            if (null == record) {
                throw new ArgumentNullException(nameof(record));
            }

            // Get attributes
            var attributes = RecordReflection.GetRosProperties(record);

            // Prepare sentence
            var sentence = new Sentence();
            sentence.Attributes = attributes;

            // Set command
            sentence.Command = RecordReflection.GetPath<T>() + "/add";

            // Remove (blank) id
            sentence.Attributes.Remove(".id");

            // Make call
            var result = Link.Call(sentence).Wait();
            if (result.IsError) {
                result.TryGetTrapAttribute("message", out var message);
                throw new CallException(message);
            }

            if (readBack) {
                // Build sentence
                sentence = new Sentence();
                sentence.Command = RecordReflection.GetPath<T>() + "/print";
                foreach (var f in attributes) {
                    // Skip records that are known to vary TODO: may need to add more here
                    if(f.Key == "interface"|| f.Key == "lease-time" || f.Key == "disabled") {
                        continue;
                    }
                    sentence.Queries.Add($"={f.Key}={f.Value}");
                }

                // Make call
                result = Link.Call(sentence).Wait();
                if (result.IsError) {
                    result.TryGetTrapAttribute("message", out var message);
                    throw new CallException(message);
                }

                // Convert record sentence to record
                var a = result.Sentences.Where(b => b.Command == "re");
                if (a.Count() != 1) {
                    throw new CallException($"Unexpected number of results returned. Expected 1, got {a.Count()}.");
                }
                RecordReflection.SetRosProperties(record, a.Single().Attributes);
            }
        }

        /// <summary>
        /// Delete a record from a set.
        /// </summary>
        /// <remarks>
        /// While it's perfectly fine to use these methods directly, it's intended that you use the vanity methods instead (eg. link.Ip.Arp.Get()).
        /// </remarks>
        /// <typeparam name="T"></typeparam>
        /// <param name="record"></param>
        public virtual void Delete(T record) {
            if (null == record) {
                throw new ArgumentNullException(nameof(record));
            }
            if (null == record.Id) {
                throw new ArgumentException("ID cannot be null.", nameof(record));
            }

            // Build sentence
            var sentence = new Sentence();
            sentence.Command = RecordReflection.GetPath<T>() + "/remove";
            sentence.Attributes = new Dictionary<string, string>() {
                {".id", record.Id }
            };

            // Make call
            var result = Link.Call(sentence).Wait();
            if (result.IsError) {
                result.TryGetTrapAttribute("message", out var message);
                throw new CallException(message);
            }
        }
    }
}
