using System;

namespace InvertedTomato.TikLink {
    public class CallException : Exception {
        public CallException() { }

        public CallException(string message) : base(message) { }

        public CallException(string message, Exception innerException) : base(message, innerException) { }
    }
}