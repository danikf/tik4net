using InvertedTomato.TikLink.Records;
using System;
using System.Collections.Generic;
using System.Text;
using System.Linq;

namespace InvertedTomato.TikLink.RecordHandlers {
    public abstract class FixedSetRecordHandlerBase<T> : RecordHandlerBase<T> where T : SetRecordBase, new() {
        internal FixedSetRecordHandlerBase(Link link) : base(link) { }
        
        /// <summary>
        /// Retreive a list of all records of a given type.
        /// </summary>
        /// <remarks>
        /// While it's perfectly fine to use these methods directly, it's intended that you use the vanity methods instead (eg. link.Ip.Arp.List()).
        /// </remarks>
        /// <typeparam name="T"></typeparam>
        /// <param name="properties">Properties to include in listing. Reduces amount of data required for the call. NULL returns all properties.</param>
        /// <param name="filter">Only include records whos fields match this filter.</param>
        /// <returns></returns>
        public IList<T> List(string[] properties = null, Dictionary<string, string> filter = null) {
            // Build sentence
            var sentence = new Sentence();
            sentence.Command = RecordReflection.GetPath<T>() + "/print";
            if (null != properties) {
                sentence.Attributes[".proplist"] = string.Join(",", properties.Select(a => RecordReflection.ResolveProperty<T>(a)));
            }
            if (null != filter) {
                foreach (var f in filter) {
                    string k;
                    try {
                        k = RecordReflection.ResolveProperty<T>(f.Key);
                    } catch (KeyNotFoundException) {
                        throw new ArgumentException($"Unknown filter field '{f.Key}'.", nameof(filter));
                    }
                    if (f.Value.Length < 1) {
                        throw new ArgumentException($"Filter value must be at least 1 character long.", nameof(filter));
                    }
                    var v = f.Value.Substring(1);
                    var op = f.Value.Substring(0, 1);
                    if (op != ">" && op != "<" && op != "=") {
                        throw new ArgumentException($"Unknown filter operation '{op}' on '{f.Key}'.", nameof(filter));
                    }
                    sentence.Queries.Add($"{op}{k}={v}");
                }
            }

            // Make call
            var result = Link.Call(sentence).Wait();
            if (result.IsError) {
                result.TryGetTrapAttribute("message", out var message);
                throw new CallException(message);
            }

            // Convert record sentences records
            var output = new List<T>();
            foreach (var s in result.Sentences) {
                if (s.Command == "re") {
                    var record = new T();
                    RecordReflection.SetRosProperties(record, s.Attributes);
                    output.Add(record);
                }
            }

            return output;
        }

        /// <summary>
        /// Retrieve a single object with a specific ID.
        /// </summary>
        /// <remarks>
        /// While it's perfectly fine to use these methods directly, it's intended that you use the vanity methods instead (eg. link.Ip.Arp.Get()).
        /// </remarks>
        /// <typeparam name="T"></typeparam>
        /// <param name="id"></param>
        /// <param name="properties">Properties to include in listing. Reduces amount of data required for the call. NULL returns all properties.</param>
        /// <returns></returns>
        public T Get(string id, string[] properties = null) {
            if (null == id) {
                throw new ArgumentNullException(nameof(id));
            }

            var scan = List(properties, new Dictionary<string, string>() { { "Id", $"={id}" } });
            if (scan.Count != 1) {
                throw new CallException($"Record with ID '{id}' not found.");
            }

            return scan.Single();
        }
    }
}
