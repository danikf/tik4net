using InvertedTomato.TikLink.Records;
using System;
using System.Collections.Generic;
using System.Text;
using System.Linq;

namespace InvertedTomato.TikLink.RecordHandlers {
    public abstract class RecordHandlerBase<T> where T : RecordBase, new() {
        protected readonly Link Link;

        internal RecordHandlerBase(Link link) {
            if (null == link) {
                throw new ArgumentNullException(nameof(link));
            }

            Link = link;
        }

        /// <summary>
        /// Update an existing record in a set.
        /// </summary>
        /// <remarks>
        /// While it's perfectly fine to use these methods directly, it's intended that you use the vanity methods instead (eg. link.Ip.Arp.Get()).
        /// </remarks>
        /// <typeparam name="T"></typeparam>
        /// <param name="record"></param>
        /// <param name="properties"></param>
        public virtual void Update(T record, string[] properties = null) {
            if (null == record) {
                throw new ArgumentNullException(nameof(record));
            }

            // Check ID is present for set records
            var r = record as SetRecordBase;
            if (null != r && r.Id == null) {
                throw new CallException("Attempting to updated a record with no ID set. Did you forget to include the Id in your query?");
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
            sentence.Command = RecordReflection.GetPath<T>() + "/set";

            // Make call
            var result = Link.Call(sentence).Wait();
            if (result.IsError) {
                result.TryGetTrapAttribute("message", out var message);
                throw new CallException(message);
            }
        }
    }
}
