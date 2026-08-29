using System.Collections.Generic;
using System.Linq;

namespace tik4net.Testing
{
    /// <summary>
    /// Fake implementation of <see cref="ITikReSentence"/> for use with <see cref="TikFakeConnection"/>.
    /// </summary>
    public sealed class TikFakeReSentence : ITikReSentence
    {
        private readonly Dictionary<string, string> _words;

        /// <summary>Sentence words (field name → value).</summary>
        public IReadOnlyDictionary<string, string> Words => _words;

        /// <summary>Tag (always null for sync fake sentences).</summary>
        public string Tag { get; }

        /// <summary>Creates a fake !re sentence from an explicit word dictionary.</summary>
        public TikFakeReSentence(Dictionary<string, string> words, string? tag = null)
        {
            _words = words ?? new Dictionary<string, string>();
            Tag = tag!; // ITikSentence.Tag is non-nullable; fake sentences have no tag (see summary)
        }

        /// <inheritdoc/>
        public string GetId() => GetResponseField(TikSpecialProperties.Id);

        /// <inheritdoc/>
        public string GetResponseField(string fieldName)
        {
            if (_words.TryGetValue(fieldName, out string? value))
                return value;
            throw new TikSentenceException($"Field '{fieldName}' not found in fake !re sentence.", this);
        }

        /// <inheritdoc/>
        public bool TryGetResponseField(string fieldName, out string fieldValue)
        {
            bool found = _words.TryGetValue(fieldName, out var v);
            fieldValue = v!; // null only when found is false; interface contract leaves the out value unspecified in that case
            return found;
        }

        /// <inheritdoc/>
        public string? GetResponseFieldOrDefault(string fieldName, string? defaultValue)
            => _words.TryGetValue(fieldName, out string? value) ? value : defaultValue;
    }

    /// <summary>
    /// Fake implementation of <see cref="ITikDoneSentence"/> for use with <see cref="TikFakeConnection"/>.
    /// </summary>
    public sealed class TikFakeDoneSentence : ITikDoneSentence
    {
        private readonly Dictionary<string, string> _words;

        /// <summary>Sentence words (e.g. ret=*1 for /add responses).</summary>
        public IReadOnlyDictionary<string, string> Words => _words;

        /// <summary>Tag (always null for sync fake sentences).</summary>
        public string Tag { get; }

        /// <summary>Creates a fake !done sentence with no words (typical for non-query responses).</summary>
        public TikFakeDoneSentence(string? tag = null)
        {
            _words = new Dictionary<string, string>();
            Tag = tag!; // ITikSentence.Tag is non-nullable; fake sentences have no tag (see summary)
        }

        /// <summary>Creates a fake !done sentence with explicit words (e.g. =ret=*1 for /add).</summary>
        public TikFakeDoneSentence(Dictionary<string, string> words, string? tag = null)
        {
            _words = words ?? new Dictionary<string, string>();
            Tag = tag!; // ITikSentence.Tag is non-nullable; fake sentences have no tag (see summary)
        }

        /// <inheritdoc/>
        public string GetResponseWord()
        {
            if (_words.TryGetValue(TikSpecialProperties.Ret, out string? value))
                return value;
            throw new TikSentenceException("No '=ret=' word in fake !done sentence.", this);
        }

        /// <inheritdoc/>
        public string? GetResponseWordOrDefault(string? defaultValue)
            => _words.TryGetValue(TikSpecialProperties.Ret, out string? value) ? value : defaultValue;
    }

    /// <summary>
    /// Fake implementation of <see cref="ITikTrapSentence"/> for use with <see cref="TikFakeConnection"/>.
    /// Use <see cref="TikFakeConnection.WithTrap"/> to register trap responses.
    /// </summary>
    public sealed class TikFakeTrapSentence : ITikTrapSentence
    {
        /// <inheritdoc/>
        public IReadOnlyDictionary<string, string> Words { get; }

        /// <inheritdoc/>
        public string Tag { get; }

        /// <inheritdoc/>
        public string CategoryCode { get; }

        /// <inheritdoc/>
        public string CategoryDescription { get; }

        /// <inheritdoc/>
        public string Message { get; }

        /// <summary>Creates a fake !trap sentence.</summary>
        /// <param name="message">Error message (e.g. "no such item", "already have such item").</param>
        /// <param name="categoryCode">Optional MikroTik error category code. Omitted means "not stated",
        /// which is the empty string on the wire — never null.</param>
        /// <param name="tag">Optional tag. Omitted means untagged, again the empty string.</param>
        /// <remarks>
        /// The three defaults here are <b>not</b> cosmetic. <see cref="ITikTrapSentence.CategoryCode"/>,
        /// <see cref="ITikTrapSentence.CategoryDescription"/> and <see cref="ITikSentence.Tag"/> are declared
        /// non-nullable, and every real transport upholds that — <c>ApiTrapSentence.CategoryDescription</c>
        /// falls back to <c>"unknown"</c> and every real sentence defaults <c>Tag</c> to the empty string.
        /// A fake that handed out nulls behind those signatures would produce, in a unit test, the one
        /// failure that cannot happen against a router: a NullReferenceException the compiler said was
        /// impossible. A test double whose contract is weaker than the real thing tests the wrong library.
        /// </remarks>
        public TikFakeTrapSentence(string message, string? categoryCode = null, string? tag = null)
        {
            Message = message;
            CategoryCode = categoryCode ?? string.Empty;
            CategoryDescription = "unknown";   // what ApiTrapSentence answers for an unrecognised code
            Tag = tag ?? string.Empty;
            Words = new Dictionary<string, string> { { "message", message } };
        }
    }
}
