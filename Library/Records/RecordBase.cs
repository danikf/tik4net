using System;
using System.Collections.Generic;

namespace InvertedTomato.TikLink.Records {
    public class RecordBase {
        [Obsolete("These properties may be removed at any time. Raise an issue at https://github.com/invertedtomato/tiklink/issues to have a property added properly.")]
        public Dictionary<string, string> OtherProperties;
    }
}
