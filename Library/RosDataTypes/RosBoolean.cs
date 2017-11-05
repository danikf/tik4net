using System;
using System.Collections.Generic;
using System.Text;

namespace InvertedTomato.TikLink.RosDataTypes {
    public class RosBoolean : IRosDataType {
        public string Encode(object localvalue, Type localtype) {
            switch (localvalue) {
                case null: return null;
                case true: return "yes";
                case false: return "no";
                default: throw new NotImplementedException();
            }
        }

        public object Decode(string rosvalue, Type localtype) {
            if (null == rosvalue) {
                throw new ArgumentNullException(nameof(rosvalue));
            }

            switch (rosvalue) {
                case "":
                    return null;
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
