using InvertedTomato.TikLink.Records;
using System;
using System.Collections.Generic;
using System.Text;
using System.Linq;

namespace InvertedTomato.TikLink.RecordHandlers {
    public abstract class FixedSetRecordHandlerBase<T> : RecordHandlerBase<T> where T : SetRecordBase, new() {
        internal FixedSetRecordHandlerBase(Link link) : base(link) { }

        [Obsolete("Use other Query() methods instead. This will be removed in a future release.")]
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
        /// Retrieve entire set of records.
        /// </summary>
        public IList<T> Query() { return Query((string[])null, (QueryFilter[])null); }

        /// <summary>
        /// Retrieve a set of records.
        /// </summary>
        /// <param name="filters">Logic to restrict records returned. If omitted ALL records of this type are returned.</param>
        public IList<T> Query(params QueryFilter[] filters) { return Query(null, filters); }

        /// <summary>
        /// Retreive a set of records.
        /// </summary>
        /// <param name="properties">Properties to return. This can vastly improve performance. If omitted ALL properties are returned.</param>
        public IList<T> Query(params string[] properties) { return Query(properties, null); }

        /// <summary>
        /// Retreieve a set of records.
        /// </summary>
        /// <param name="properties">Properties to return. This can vastly improve performance. If omitted ALL properties are returned.</param>
        /// <param name="filters">Logic to restrict records returned. If omitted ALL records of this type are returned.</param>
        public IList<T> Query(string[] properties, QueryFilter[] filters) {
            var sentence = new Sentence();
            sentence.Command = RecordReflection.GetPath<T>() + "/print";

            // Add 'detail' flag (performance is compensated by .proplist)
            sentence.Attributes["detail"] = string.Empty;

            // Attach property list
            if (null != properties) {
                sentence.Attributes[".proplist"] = string.Join(",", properties.Select(a => RecordReflection.ResolveProperty<T>(a)));
            }

            // Attach filters
            if (null != filters) {
                foreach (var filter in filters) {
                    string rosProperty;
                    try {
                        rosProperty = RecordReflection.ResolveProperty<T>(filter.Property);
                    } catch (KeyNotFoundException) {
                        throw new ArgumentException($"Unknown filter property '{filter.Property}'.", nameof(filter));
                    }
                    sentence.Queries.Add($"{(char)filter.Operation}{rosProperty}={filter.Value.ToString()}");
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

        public T QueryById(string id, params string[] properties) {
            if (null == id) {
                throw new ArgumentNullException(nameof(id));
            }

            // If there's no properties, set to null, otherwise we'll get 0 properties
            if (properties.Length == 0) {
                properties = null;
            }

            // Execute query
            var records = Query(
                properties,
                new QueryFilter[] { new QueryFilter("Id", QueryOperationType.Equal, id) }
            );

            // Unwrap single result
            if (records.Count != 1) {
                throw new CallException($"Record with ID '{id}' not found.");
            }
            return records.Single();
        }

        public T QueryByName(string name, params string[] properties) {
            if (null == name) {
                throw new ArgumentNullException(nameof(name));
            }

            // If there's no properties, set to null, otherwise we'll get 0 properties
            if (properties.Length == 0) {
                properties = null;
            }

            // Eecute query
            var records = Query(
                properties,
                new QueryFilter[] { new QueryFilter("Name", QueryOperationType.Equal, name) }
            );

            // Unwrap single result
            if (records.Count != 1) {
                throw new CallException($"Expecting 1 record with name '{name}', instead {records.Count} found.");
            }
            return records.Single();
        }
    }
}
