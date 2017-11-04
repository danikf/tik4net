using System;

namespace InvertedTomato.TikLink.Encodings {
    public static class LongEncoding {
        public static string EncodeNullable(long? value) {
            if (null == value) {
                return string.Empty;
            }

            return Encode(value.Value);
        }

        public static string Encode(long value) {
            return value.ToString();
        }

        public static long? DecodeNullable(string value) {
            if (null == value) {
                throw new ArgumentNullException(nameof(value));
            }

            if (value == string.Empty) {
                return null;
            }

            return Decode(value);
        }

        public static long Decode(string value) {
            if (null == value) {
                throw new ArgumentNullException(nameof(value));
            }

            return long.Parse(value);
        }
    }
}
