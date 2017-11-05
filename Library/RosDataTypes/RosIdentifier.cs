using System;

namespace InvertedTomato.TikLink.RosDataTypes {
    public class RosIdentifier : IRosDataType {
        public string Encode(object localvalue, Type localtype) {
            return (string)localvalue;
            /*
            var v = (long?)localvalue;
            if (null == v) {
                return null;
            }

            return "*" + v.ToString();*/
        }

        public object Decode(string rosvalue, Type localtype) {
            if (null == rosvalue) {
                throw new ArgumentNullException(nameof(rosvalue));
            }

            return rosvalue;

            /*
            if (rosvalue == string.Empty) {
                return null;
            }

            if (rosvalue.Substring(0, 1) != "*") {
                throw new FormatException("Identifier not starting with '*'");
            }

            return long.Parse(rosvalue.Substring(1));*/
        }
    }
}
