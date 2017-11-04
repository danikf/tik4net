using System;
using System.Text.RegularExpressions;

namespace InvertedTomato.TikLink.Encodings {
    /// <summary>
    /// Support for converting MikroTik time strings into TimeSpans.
    /// Credits: D-Bullock 
    /// </summary>
    public static class TimeEncoding {
        private static readonly Regex Pattern = new Regex(@"((\d+)w)?((\d+)d)?((\d+)h)?((\d+)m)?((\d+)s)?((\d+)ms)?", RegexOptions.Compiled);
        
        /// <summary>
        /// Encode nullable TimeSpan into a MikroTik time string.
        /// </summary>
        public static string EncodeNullable(double? value) {
            // Handle nulls
            if (null == value) {
                return string.Empty;
            }

            return Encode(value.Value);
        }

        /// <summary>
        /// Encode TimeSpan into a MikroTik time string.
        /// </summary>
        public static string Encode(double value) {
            var v = TimeSpan.FromSeconds(value);
            var weeks = (long)v.TotalDays / 7;
            v -= TimeSpan.FromDays(weeks * 7);
            return
                (weeks != 0 ? weeks + "w" : string.Empty) +
                (v.Days != 0 ? v.Days + "d" : string.Empty) +
                (v.Hours != 0 ? v.Hours + "h" : string.Empty) +
                (v.Minutes != 0 ? v.Minutes + "m" : string.Empty) +
                (v.Seconds != 0 ? v.Seconds + "s" : string.Empty);
        }

        /// <summary>
        /// Decode MikroTik time string into a nullable TimeSpan.
        /// </summary>
        public static double? DecodeNullable(string value) {
            // Handle blank/null
            if (value == string.Empty || value == "none") {
                return null;
            }

            return Decode(value);
        }
        
        /// <summary>
        /// Decode MikroTik time string into a TimeSpan.
        /// </summary>
        public static double Decode(string value) {
            // Parse
            var match = Pattern.Match(value);
            if (!match.Success) {
                throw new FormatException();
            }

            // Process components
            double ms = 0;
            for (int i = 1; i < match.Groups.Count; i += 2) {
                if (!string.IsNullOrEmpty(match.Groups[i].Value)) {
                    var v = double.Parse(match.Groups[i + 1].Value);
                    if (match.Groups[i].Value.EndsWith("w")) {
                        ms += v * 604800;
                    } else if (match.Groups[i].Value.EndsWith("d")) {
                        ms += v * 86400;
                    } else if (match.Groups[i].Value.EndsWith("h")) {
                        ms += v * 3600;
                    } else if (match.Groups[i].Value.EndsWith("m")) {
                        ms += v * 60;
                    } else if (match.Groups[i].Value.EndsWith("s")) {
                        ms += v ;
                    } else if (match.Groups[i].Value.EndsWith("ms")) {
                        ms += v/1000;
                    }
                }
            }

            return ms;
        }
    }
}
