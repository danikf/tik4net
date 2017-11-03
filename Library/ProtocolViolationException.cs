using System;

namespace InvertedTomato.TikLink {
    internal class ProtocolViolationException : Exception {
        public ProtocolViolationException() {
        }

        public ProtocolViolationException(string message) : base(message) {
        }

        public ProtocolViolationException(string message, Exception innerException) : base(message, innerException) {
        }
    }
}