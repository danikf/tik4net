using System.Collections.Generic;

namespace tik4net.Rest
{
    internal class RestTrapSentence : ITikTrapSentence
    {
        public string Message { get; }
        public string CategoryCode { get; }
        public string CategoryDescription { get; }

        internal RestTrapSentence(string message, string categoryCode = null, string categoryDescription = null)
        {
            Message = message;
            CategoryCode = categoryCode;
            CategoryDescription = categoryDescription;
        }

        public IReadOnlyDictionary<string, string> Words
            => new Dictionary<string, string> { ["message"] = Message ?? string.Empty };

        public string Tag => string.Empty;
    }
}
