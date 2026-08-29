namespace tik4net
{
    /// <summary>
    /// What a <see cref="TikDuration"/> / <see cref="TikDataRate"/> is actually holding — a value, one of
    /// the words RouterOS uses in place of one, or text the type could not read.
    /// </summary>
    /// <remarks>
    /// The last two used to be one state, and conflating them cost twice. A caller had no way to ask "is
    /// this field turned off?" except by comparing the raw word, and — worse — <b>a gap in our own parsing
    /// was invisible</b>: unreadable text became a token, round-tripped through
    /// <see cref="object.ToString"/> perfectly, and nobody ever learned the type had failed to read it.
    /// <para>
    /// <see cref="Special"/> is a word the library recognises as a real router state.
    /// <see cref="Unknown"/> is the honest fallback — the value survives verbatim, so nothing is lost and
    /// the entity still loads, but it also raises a <c>value.token</c> note on
    /// <see cref="Diagnostics.TikWireTrace"/> so the gap can be found instead of guessed at.
    /// </para>
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
        /// Text the type could not read and does not recognise as a router word. Kept verbatim in
        /// <c>Token</c> and written back unchanged, so the round trip is safe — but this is a gap in the
        /// library, not a state of the router, and it is traced as one.
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
