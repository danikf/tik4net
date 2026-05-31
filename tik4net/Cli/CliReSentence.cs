using System.Collections.Generic;

namespace tik4net.Cli
{
    internal class CliReSentence : ITikReSentence
    {
        private readonly Dictionary<string, string> _fields;

        internal CliReSentence(Dictionary<string, string> fields)
        {
            _fields = fields;
        }

        public IReadOnlyDictionary<string, string> Words => _fields;

        public string Tag => string.Empty;

        public string GetId() => GetResponseField(TikSpecialProperties.Id);

        public string GetResponseField(string fieldName)
        {
            if (_fields.TryGetValue(fieldName, out var v))
                return v;
            throw new TikSentenceException($"Missing field '{fieldName}'.", this);
        }

        public string GetResponseFieldOrDefault(string fieldName, string defaultValue)
            => _fields.TryGetValue(fieldName, out var v) ? v : defaultValue;

        public bool TryGetResponseField(string fieldName, out string fieldValue)
            => _fields.TryGetValue(fieldName, out fieldValue);
    }
}
