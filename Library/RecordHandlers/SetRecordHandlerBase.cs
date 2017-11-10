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
        /// <param name="record"></param>
        /// <param name="properties"></param>
        public virtual void Add(T record, string[] properties = null) {
            if (null == record) {
                throw new ArgumentNullException(nameof(record));
            }

            // Get attributes
            var attributes = RecordReflection.GetRosProperties(record);

            // If filtering properties, remove attributes not wanted
            if (null != properties) {
                var remove = attributes.Keys.Where(a => !properties.Contains(RecordReflection.ResolveProperty<T>(a))).ToList();
                foreach (var k in remove) {
                    attributes.Remove(k);
                }
            }

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
        }
        
        /// <summary>
        /// Delete a record from a set.
        /// </summary>
        /// <remarks>
        /// While it's perfectly fine to use these methods directly, it's intended that you use the vanity methods instead (eg. link.Ip.Arp.Get()).
        /// </remarks>
        /// <typeparam name="T"></typeparam>
        /// <param name="id"></param>
        public virtual void Delete(string id) {
            if (null == id) {
                throw new ArgumentNullException(nameof(id));
            }

            // Build sentence
            var sentence = new Sentence();
            sentence.Command = RecordReflection.GetPath<T>() + "/remove";
            sentence.Attributes = new Dictionary<string, string>() {
                {".id", id }
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
