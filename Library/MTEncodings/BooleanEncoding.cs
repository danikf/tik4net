using System;
using System.Collections.Generic;
using System.Text;

namespace InvertedTomato.TikLink.MTEncodings {
    public static class BooleanEncoding {
        public static string EncodeNullable(bool? value) {
            if (null == value) {
                return string.Empty;
            }

            return Encode(value.Value);
        }

        public static string Encode(bool value) {
            return value ? "yes" : "no";
        }

        public static bool? DecodeNullable(string value) {
            if (null == value) {
                throw new ArgumentNullException(nameof(value));
            }

            if(value == string.Empty) {
                return null;
            }

            return Decode(value);
        }
        public static bool Decode(string value) {
            if (null == value) {
                throw new ArgumentNullException(nameof(value));
            }

            switch (value) {
                case "true":
                case "yes":
                    return true;
                case "false":
                case "no":
                    return false;
                default: throw new FormatException();
            }
        }
    }
}
