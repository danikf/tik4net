using System;
using System.Text.RegularExpressions;

namespace InvertedTomato.TikLink {
    /// <summary>
    /// Support for converting MikroTik time strings into TimeSpans.
    /// Credits: D-Bullock 
    /// </summary>
    public static class TimeEncoding {
        private static readonly Regex Pattern = new Regex(@"((\d+)w)?((\d+)d)?((\d+)h)?((\d+)m)?((\d+)s)?((\d+)ms)?", RegexOptions.Compiled);
        
        /// <summary>
        /// Encode nullable TimeSpan into a MikroTik time string.
        /// </summary>
        public static string EncodeNullable(TimeSpan? value) {
            // Handle nulls
            if (null == value) {
                return string.Empty;
            }

            return Encode(value.Value);
        }

        /// <summary>
        /// Encode TimeSpan into a MikroTik time string.
        /// </summary>
        public static string Encode(TimeSpan value) {
            var weeks = (long)value.TotalDays / 7;
            value -= TimeSpan.FromDays(weeks * 7);
            return
                (weeks != 0 ? weeks + "w" : string.Empty) +
                (value.Days != 0 ? value.Days + "d" : string.Empty) +
                (value.Hours != 0 ? value.Hours + "h" : string.Empty) +
                (value.Minutes != 0 ? value.Minutes + "m" : string.Empty) +
                (value.Seconds != 0 ? value.Seconds + "s" : string.Empty);
        }

        /// <summary>
        /// Decode MikroTik time string into a nullable TimeSpan.
        /// </summary>
        public static TimeSpan? DecodeNullable(string time) {
            // Handle blank/null
            if (time == string.Empty || time == "none") {
                return null;
            }

            return Decode(time);
        }
        
        /// <summary>
        /// Decode MikroTik time string into a TimeSpan.
        /// </summary>
        public static TimeSpan? Decode(string time) {
            // Parse
            var match = Pattern.Match(time);
            if (!match.Success) {
                throw new FormatException();
            }

            // Process components
            double ms = 0;
            for (int i = 1; i < match.Groups.Count; i += 2) {
                if (!string.IsNullOrEmpty(match.Groups[i].Value)) {
                    var value = double.Parse(match.Groups[i + 1].Value);
                    if (match.Groups[i].Value.EndsWith("w")) {
                        ms += value * 604800000;
                    } else if (match.Groups[i].Value.EndsWith("d")) {
                        ms += value * 86400000;
                    } else if (match.Groups[i].Value.EndsWith("h")) {
                        ms += value * 3600000;
                    } else if (match.Groups[i].Value.EndsWith("m")) {
                        ms += value * 60000;
                    } else if (match.Groups[i].Value.EndsWith("ms")) {
                        ms += value;
                    } else if (match.Groups[i].Value.EndsWith("s")) {
                        ms += value * 1000;
                    }
                }
            }

            return TimeSpan.FromMilliseconds(ms);
        }
    }
}
