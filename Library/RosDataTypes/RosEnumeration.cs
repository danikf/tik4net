using System;
using System.Collections.Generic;
using System.Reflection;

namespace InvertedTomato.TikLink.RosDataTypes {
    public class RosEnumeration : IRosDataType {
        public string Encode(object localvalue, Type type) {
            if (null == localvalue) {
                return null;
            }

            foreach (var field in type.GetRuntimeFields()) {
                var attribute = field.GetCustomAttribute<RosEnumAttribute>(true);
                if (attribute != null && field.Name == localvalue.ToString()) {
                    return attribute.Value;
                }
            }
            return localvalue.ToString();
        }

        public object Decode(string rosvalue, Type type) {
            if (!type.GetTypeInfo().IsEnum) {
                throw new InvalidOperationException();
            }

            foreach (var field in type.GetRuntimeFields()) {
                var attribute = field.GetCustomAttribute<RosEnumAttribute>(true);
                if (attribute != null && attribute.Value == rosvalue) {
                    return field.GetValue(null);
                }
            }

            return null;
        }
    }
}
