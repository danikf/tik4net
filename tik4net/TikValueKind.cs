namespace tik4net
{
    /// <summary>
    /// What a <see cref="TikDuration"/> / <see cref="TikDataRate"/> is actually holding — a value, one of
    /// the words RouterOS uses in place of one, or a word only the caller vouches for.
    /// </summary>
    /// <remarks>
    /// <see cref="Special"/> is a word the library recognises as a real router state. <see cref="Unknown"/> is a word
    /// the caller passed to <c>FromToken</c>; <c>Parse</c> refuses such a word, so a read never makes one.
    /// </remarks>
    public enum TikValueKind
    {
        /// <summary>Nothing at all — the type's default, and what an absent field maps to.</summary>
        Empty = 0,

        /// <summary>A real value: a <see cref="System.TimeSpan"/>, or a number of bits per second.</summary>
        Value = 1,

        /// <summary>
        /// One of the words RouterOS uses instead of a value, recognised by the library — read it from
        /// <c>Special</c>.
        /// </summary>
        Special = 2,

        /// <summary>
        /// A word the library does not recognise, given to <c>FromToken</c>. Kept verbatim in <c>Token</c> and written
        /// as it is.
        /// </summary>
        Unknown = 3,
    }

    /// <summary>
    /// The words RouterOS writes into a duration field instead of a length of time.
    /// </summary>
    /// <remarks>
    /// Per <b>type</b>, not per field: the property is a shared <see cref="TikDuration"/>, so it can only
    /// know which words durations use, not which subset this particular menu accepts. The router refuses a
    /// word its field does not take, which is the check that matters.
    /// </remarks>
    public enum TikDurationSpecial
    {
        /// <summary><c>none</c> — no duration set at all.</summary>
        None = 0,

        /// <summary><c>disabled</c> — the timer this field drives is off.</summary>
        Disabled = 1,

        /// <summary><c>auto</c> — RouterOS picks the duration itself.</summary>
        Auto = 2,

        /// <summary><c>never</c> — the event this field times never happens.</summary>
        Never = 3,

        /// <summary><c>immediately</c> — no wait at all, as a word rather than as a zero.</summary>
        Immediately = 4,

        /// <summary><c>forever</c> — no expiry.</summary>
        Forever = 5,
    }

    /// <summary>
    /// The words RouterOS writes into a rate field instead of a number of bits per second.
    /// </summary>
    /// <remarks>Per type rather than per field, for the same reason as <see cref="TikDurationSpecial"/>.</remarks>
    public enum TikDataRateSpecial
    {
        /// <summary><c>unlimited</c> — no rate limit; the default of several bandwidth fields.</summary>
        Unlimited = 0,

        /// <summary><c>auto</c> — RouterOS picks the rate itself, e.g. a negotiated link speed.</summary>
        Auto = 1,

        /// <summary><c>none</c> — no rate set at all.</summary>
        None = 2,
    }
}
