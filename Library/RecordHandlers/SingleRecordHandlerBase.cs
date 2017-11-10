using InvertedTomato.TikLink.Records;
using System;
using System.Collections.Generic;
using System.Text;
using System.Linq;

namespace InvertedTomato.TikLink.RecordHandlers {
    public abstract class SingleRecordHandlerBase<T> : RecordHandlerBase<T> where T : SingleRecordBase, new() {
        internal SingleRecordHandlerBase(Link link) : base(link) { }

        public T Get(string[] properties = null) {
            // Build sentence
            var sentence = new Sentence();
            sentence.Command = RecordReflection.GetPath<T>() + "/print";
            if (null != properties) {
                sentence.Attributes[".proplist"] = string.Join(",", properties.Select(a => RecordReflection.ResolveProperty<T>(a)));
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

            if (output.Count != 1) {
                throw new CallException($"Record with not found.");
            }

            return output.Single();
        }
    }
}