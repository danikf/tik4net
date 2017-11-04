using System;

namespace InvertedTomato.TikLink {
    internal class QueryException : Exception {
        public QueryException() { }

        public QueryException(string message) : base(message) { }

        public QueryException(string message, Exception innerException) : base(message, innerException) { }
    }
}