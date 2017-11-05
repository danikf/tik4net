using System;

namespace InvertedTomato.TikLink.RosDataTypes {
    public class RosMacAddress : IRosDataType {
        public string Encode(object localvalue, Type localtype) {
            return (string)localvalue;
        }

        public object Decode(string rosvalue, Type localtype) {
            if (null == rosvalue) {
                throw new ArgumentNullException(nameof(rosvalue));
            }

            return rosvalue;
        }
    }
}
