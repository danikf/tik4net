using System;
using System.Collections.Generic;
using System.Reflection;

namespace InvertedTomato.TikLink.MTEncodings {
    public static class EnumEncoding {
        public static string EncodeNullable(Enum value) {
            if (null == value) {
                return string.Empty;
            }

            return Encode(value);
        }

        public static string Encode(Enum value) {
            if (null == value) {
                throw new ArgumentNullException(nameof(value));
            }

            return value.ToString();
        }

        public static object DecodeNullable(string value, Type type) {
            if (!type.GetTypeInfo().IsEnum) {
                throw new InvalidOperationException();
            }

            foreach (var field in type.GetRuntimeFields()) {
                var attribute = field.GetCustomAttribute<TikEnumAttribute>(true);
                if (attribute == null) {
                    if (field.Name == value) {
                        return field.GetValue(null);
                    }
                } else {
                    if (attribute.Value == value) {
                        return field.GetValue(null);
                    }
                }
            }

            return null;
        }

        public static object Decode(string value, Type type) {
            var v = DecodeNullable(value, type);
            if (null == v) {
                throw new KeyNotFoundException();
            }
            return v;
        }


    }
}
