using System;

namespace InvertedTomato.TikLink {
    public class QueryFilter {
        public string Property { get; private set; }
        public QueryOperationType Operation { get; private set; }
        public object Value { get; private set; }

        public QueryFilter(string property, QueryOperationType operation, object value) {
            if (null == property) {
                throw new ArgumentNullException(nameof(property));
            }
            if (null == value) {
                throw new ArgumentNullException(nameof(value));
            }

            Property = property;
            Operation = operation;
            Value = value;
        }

        public override string ToString() {
            return $"{Property}{(char)Operation}{Value}";
        }
    }
}