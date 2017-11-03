using System;

namespace InvertedTomato.TikLink {
    internal class QueryFailedException : Exception {
        public QueryFailedException() { }

        public QueryFailedException(string message) : base(message) { }

        public QueryFailedException(string message, Exception innerException) : base(message, innerException) { }
    }
}