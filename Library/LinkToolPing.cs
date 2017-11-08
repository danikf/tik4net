using InvertedTomato.TikLink.RosRecords;
using System;
using System.Collections.Generic;
using System.Linq;

namespace InvertedTomato.TikLink {
    public class LinkToolPing {
        private readonly Link Link;

        internal LinkToolPing(Link link) {
            Link = link;
        }

        public bool IsAlive(string address, int attempts = 1) {
            if (attempts < 1) {
                throw new ArgumentOutOfRangeException(nameof(attempts));
            }

            return Run(address, attempts).Any(a => a.Received > 0);
        }


        public IList<ToolPing> Run(string address, int count = 1, int size = 64) {
            if (null == address) {
                throw new ArgumentNullException(nameof(address));
            }
            if (count < 1) {
                throw new ArgumentOutOfRangeException(nameof(count));
            }
            if (size < 1) {
                throw new ArgumentOutOfRangeException(nameof(size));
            }

            // Make call
            var result = Link.Call("/ping", new Dictionary<string, string>() {
                { "address", address },
                {"count", count.ToString() },
                {"size", size.ToString() }
            }).Wait();

            // Handle any error
            if (result.IsError) {
                result.TryGetTrapAttribute("message", out var message);
                throw new CallException(message);
            }

            // Convert record sentences records
            var output = new List<ToolPing>();
            foreach (var s in result.Sentences) {
                if (s.Command == "re") {
                    var record = new ToolPing();
                    RecordReflection.SetRosProperties(record, s.Attributes);
                    output.Add(record);
                }
            }

            return output;
        }
    }
}
