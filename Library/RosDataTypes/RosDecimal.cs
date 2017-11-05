using System;

namespace InvertedTomato.TikLink.RosDataTypes {
    public class RosDecimal : IRosDataType {
        public string Encode(object localvalue, Type localtype) {
            if (null == localvalue) {
                return null;
            }

            return localvalue.ToString();
        }

        public object Decode(string rosvalue, Type localtype) {
            if (null == rosvalue) {
                throw new ArgumentNullException(nameof(rosvalue));
            }

            if (rosvalue == string.Empty) {
                return null;
            }

            return double.Parse(rosvalue);
        }
    }
}
