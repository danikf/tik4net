using System;
using System.Collections.Generic;
using System.Text;

namespace InvertedTomato.TikLink.RosDataTypes {
    public interface IRosDataType {
        string Encode(object localvalue, Type localtype);
        object Decode(string rosvalue, Type localtype);
    }
}
