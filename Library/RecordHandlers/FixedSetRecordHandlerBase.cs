using InvertedTomato.TikLink.Records;
using System;
using System.Collections.Generic;
using System.Text;
using System.Linq;

namespace InvertedTomato.TikLink.RecordHandlers {
    public abstract class FixedSetRecordHandlerBase<T> : RecordHandlerBase<T> where T : SetRecordBase, new() {
        internal FixedSetRecordHandlerBase(Link link) : base(link) { }
        
        [Obsolete("Use other query methods instead. This will be removed in a future release.")]
        public IList<T> Query(Dictionary<string, string> filter = null, string[] properties = null) {
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
        /// Retrieve a set of records, with an optional filter
        /// </summary>
        /// <param name="mode">Use QUICK for a fast response, or DETAILED for a comprehensive set of fields.</param>
        /// <param name="filters">Filters to limit which records are returned.</param>
        public IList<T> Query(QueryModeType mode, params QueryFilter[] filters) { return Query(mode, null, filters); }

        /// <summary>
        /// Retrieve a set of records, with an optional filter
        /// </summary>
        /// <param name="mode">Use QUICK for a fast response, or DETAILED for a comprehensive set of fields.</param>
        /// <param name="properties">List of properties to be returned.</param>
        /// <param name="filters">Filters to limit which records are returned.</param>
        public IList<T> Query(QueryModeType mode, string[] properties = null, params QueryFilter[] filters) {
            var sentence = new Sentence();
            sentence.Command = RecordReflection.GetPath<T>() + "/print";

            if (mode == QueryModeType.Detailed) {
                sentence.Attributes["detailed"] = string.Empty;
            }

            if (null != properties) {
                sentence.Attributes[".proplist"] = string.Join(",", properties.Select(a => RecordReflection.ResolveProperty<T>(a)));
            }

            foreach (var filter in filters) {
                string rosProperty;
                try {
                    rosProperty = RecordReflection.ResolveProperty<T>(filter.Property);
                } catch (KeyNotFoundException) {
                    throw new ArgumentException($"Unknown filter property '{filter.Property}'.", nameof(filter));
                }
                sentence.Queries.Add($"{(char)filter.Operation}{rosProperty}={filter.Value.ToString()}");
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

        public T QueryById(QueryModeType mode, string id) {
            if (null == id) {
                throw new ArgumentNullException(nameof(id));
            }

            var records = Query(mode, new QueryFilter("Id", QueryOperationType.Equal, id));
            if (records.Count != 1) {
                throw new CallException($"Record with ID '{id}' not found.");
            }
            return records.Single();
        }

        public T QueryByName(QueryModeType mode, string name) {
            if (null == name) {
                throw new ArgumentNullException(nameof(name));
            }

            var records = Query(mode, new QueryFilter("Name", QueryOperationType.Equal, name));
            if (records.Count != 1) {
                throw new CallException($"Expecting 1 record with name '{name}', instead {records.Count} found.");
            }
            return records.Single();
        }
    }
}
