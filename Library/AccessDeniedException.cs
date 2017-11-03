using System;

namespace InvertedTomato.TikLink {
    public class AccessDeniedException : Exception {
        public AccessDeniedException() {
        }

        public AccessDeniedException(string message) : base(message) {
        }

        public AccessDeniedException(string message, Exception innerException) : base(message, innerException) {
        }
    }
}