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

        /// <summary>
        /// Builds the trap a background monitor/async worker reports when its poll threw. The exception object
        /// itself cannot cross the callback (the contract is a trap sentence), so this is the only copy of the
        /// reason that survives — and <c>ex.Message</c> alone loses the half that says WHOSE fault it was: a
        /// router trap, a socket error and a <c>NullReferenceException</c> in our own decoder all reach the
        /// caller as an unattributed sentence (P2.25, the same defect fixed in <c>ApiConnection</c>'s synthetic
        /// <c>!fatal</c>). Exceptions that already carry the router's own wording keep it verbatim; anything
        /// else is prefixed with its type and carries its inner exception.
        /// </summary>
        internal static TikTrapSentenceResult FromException(System.Exception ex)
        {
            if (ex == null) return new TikTrapSentenceResult("unknown error");
            // A trap exception's message IS the router's own wording (TikNoSuchItemException and friends all
            // derive from it) — prefixing it with a CLR type name would only obscure it.
            if (ex is TikCommandTrapException)
                return new TikTrapSentenceResult(ex.Message);

            string text = ex.GetType().Name + ": " + ex.Message;
            for (var inner = ex.InnerException; inner != null; inner = inner.InnerException)
                text += " -> " + inner.GetType().Name + ": " + inner.Message;
            return new TikTrapSentenceResult(text);
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

        /// <summary>
        /// When <c>true</c>, <see cref="CommandText"/> is a <b>raw</b> transport-dialect payload that must be sent
        /// verbatim, bypassing the structured CLI builder/mapper (see <c>CreateRawCommand</c> and the
        /// <see cref="TikConnectionCapability.RawCommand"/> capability). <see cref="Parameters"/> is ignored.
        /// </summary>
        internal bool IsRaw { get; }

        /// <summary>
        /// Raw-mode convenience: when <c>true</c>, the transport wraps the raw payload so it materialises a
        /// machine-readable <c>as-value</c> line (CLI: <c>:put [ … as-value]</c>) before parsing. No effect when
        /// <see cref="IsRaw"/> is <c>false</c>.
        /// </summary>
        internal bool WrapAsValue { get; }

        internal TikCommandDescriptor(string commandText, IList<ITikCommandParameter> parameters,
            bool isRaw = false, bool wrapAsValue = false)
        {
            CommandText = commandText;
            Parameters = parameters;
            IsRaw = isRaw;
            WrapAsValue = wrapAsValue;
        }
    }

    /// <summary>
    /// Implemented by command objects that can carry a <b>raw</b> pass-through flag (see
    /// <c>CreateRawCommand</c>). Lets the (assembly-internal) factory mark a command raw without widening the
    /// public <see cref="ITikCommand"/> surface. Transports that cannot send a string-raw payload simply do
    /// not declare <see cref="TikConnectionCapability.RawCommand"/>, so the factory fails closed before a raw
    /// command is ever built for them.
    /// </summary>
    internal interface ITikRawCommand
    {
        /// <summary>Send <see cref="ITikCommand.CommandText"/> verbatim in the transport dialect, no builder.</summary>
        bool IsRaw { get; set; }
        /// <summary>Wrap the raw payload so it yields an <c>as-value</c> line before parsing (CLI convenience).</summary>
        bool WrapAsValue { get; set; }
    }
}
