using System;

namespace InvertedTomato.TikLink {
    internal class PropertyConverstionException : Exception {
        public PropertyConverstionException() {
        }

        public PropertyConverstionException(string message) : base(message) {
        }

        public PropertyConverstionException(string message, Exception innerException) : base(message, innerException) {
        }
    }
}