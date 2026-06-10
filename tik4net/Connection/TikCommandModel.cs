using System.Collections.Generic;

namespace tik4net.Connection
{
    /// <summary>
    /// Transport-neutral command parameter (name/value/format) used by
    /// <see cref="TikCommandConnectionBase"/> and its command implementation.
    /// </summary>
    internal class TikCommandParameter : ITikCommandParameter
    {
        /// <inheritdoc/>
        public string Name { get; set; }
        /// <inheritdoc/>
        public string Value { get; set; }
        /// <inheritdoc/>
        public TikCommandParameterFormat ParameterFormat { get; set; }

        internal TikCommandParameter(string name, string value, TikCommandParameterFormat format = TikCommandParameterFormat.Default)
        {
            Name = name;
            Value = value;
            ParameterFormat = format;
        }

        /// <inheritdoc/>
        public override string ToString() => $"{Name}={Value}";
    }

    /// <summary>
    /// Transport-neutral <c>!re</c> record sentence backed by a field dictionary.
    /// Produced by command connections that build records in-memory (CLI parsing, native M2 decode).
    /// </summary>
    internal class TikRecordSentence : ITikReSentence
    {
        private readonly Dictionary<string, string> _fields;

        internal TikRecordSentence(Dictionary<string, string> fields)
        {
            _fields = fields;
        }

        /// <inheritdoc/>
        public IReadOnlyDictionary<string, string> Words => _fields;

        /// <inheritdoc/>
        public string Tag => string.Empty;

        /// <inheritdoc/>
        public string GetId() => GetResponseField(TikSpecialProperties.Id);

        /// <inheritdoc/>
        public string GetResponseField(string fieldName)
        {
            if (_fields.TryGetValue(fieldName, out var v))
                return v;
            throw new TikSentenceException($"Missing field '{fieldName}'.", this);
        }

        /// <inheritdoc/>
        public string GetResponseFieldOrDefault(string fieldName, string defaultValue)
            => _fields.TryGetValue(fieldName, out var v) ? v : defaultValue;

        /// <inheritdoc/>
        public bool TryGetResponseField(string fieldName, out string fieldValue)
            => _fields.TryGetValue(fieldName, out fieldValue);
    }

    /// <summary>
    /// Transport-neutral <c>!done</c> sentence with an optional return value.
    /// </summary>
    internal class TikDoneSentenceResult : ITikDoneSentence
    {
        private readonly string _ret;

        internal TikDoneSentenceResult(string ret = null)
        {
            _ret = ret;
        }

        /// <inheritdoc/>
        public IReadOnlyDictionary<string, string> Words
        {
            get
            {
                var d = new Dictionary<string, string>();
                if (_ret != null)
                    d[TikSpecialProperties.Ret] = _ret;
                return d;
            }
        }

        /// <inheritdoc/>
        public string Tag => string.Empty;

        /// <inheritdoc/>
        public string GetResponseWord()
        {
            if (_ret == null)
                throw new TikSentenceException("No return value in !done.", this);
            return _ret;
        }

        /// <inheritdoc/>
        public string GetResponseWordOrDefault(string defaultValue) => _ret ?? defaultValue;
    }

    /// <summary>
    /// Transport-neutral <c>!trap</c> error sentence.
    /// </summary>
    internal class TikTrapSentenceResult : ITikTrapSentence
    {
        /// <inheritdoc/>
        public string Message { get; }
        /// <inheritdoc/>
        public string CategoryCode { get; }
        /// <inheritdoc/>
        public string CategoryDescription { get; }

        internal TikTrapSentenceResult(string message, string categoryCode = null, string categoryDescription = null)
        {
            Message = message;
            CategoryCode = categoryCode;
            CategoryDescription = categoryDescription;
        }

        /// <inheritdoc/>
        public IReadOnlyDictionary<string, string> Words
            => new Dictionary<string, string> { ["message"] = Message ?? string.Empty };

        /// <inheritdoc/>
        public string Tag => string.Empty;
    }

    /// <summary>
    /// Lightweight descriptor passed from <see cref="TikGenericCommand"/> to the connection's
    /// CRUD hooks. Avoids exposing the full command internals to the connection.
    /// </summary>
    internal sealed class TikCommandDescriptor
    {
        internal string CommandText { get; }
        internal IList<ITikCommandParameter> Parameters { get; }

        internal TikCommandDescriptor(string commandText, IList<ITikCommandParameter> parameters)
        {
            CommandText = commandText;
            Parameters = parameters;
        }
    }
}
