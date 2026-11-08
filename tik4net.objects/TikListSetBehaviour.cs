namespace tik4net.Objects
{
    /// <summary>
    /// What RouterOS's text <c>set</c> does with the members of a <see cref="TikNegatableValue{T}"/> list that the new
    /// value does not name, declared by <see cref="TikPropertyAttribute.SetBehaviour"/>.
    /// </summary>
    /// <remarks>
    /// Every behaviour other than <see cref="Exact"/> describes a list whose members are negated one by one, so it needs a
    /// <c>TikField&lt;TikValueList&lt;TikNegatableValue&lt;T&gt;&gt;?&gt;</c> property (or its
    /// <see cref="TikNegatableField{T}"/> form); any other declaration, and a value this enum does not define, is refused
    /// when the entity is first used. <c>Docs/findings-value-lists.md</c> §3 and §4 hold the measurements.
    /// </remarks>
    public enum TikListSetBehaviour
    {
        /// <summary>
        /// The router holds exactly the value sent. The default, and what every field without a declaration is.
        /// </summary>
        Exact = 0,

        /// <summary>
        /// A text <c>set</c> replaces only the half it names — the plain members when the value names a plain one, the
        /// negated members when it names a negated one — and keeps the other half (<c>tcp-flags</c>).
        /// </summary>
        /// <remarks>
        /// <para>Measured on 6.49.13 and 7.24.5 over the API, REST and the CLI: <c>!,syn,!ack</c> + <c>set rst</c> →
        /// <c>rst,!ack</c> (<c>Docs/findings-value-lists.md</c> §4).</para>
        /// <para>An update of a loaded row that would leave such a half on the router — the loaded value has members of a
        /// kind the new one has none of — is refused before anything is sent, unless the transport writes structured data
        /// (<see cref="TikConnectionCapability.StructuredWrites"/>). A value naming both kinds, and any add, is exact.</para>
        /// <para>Not caught: <c>unset</c> hides the field but keeps both halves on the router, so after an unset the next
        /// one-kind <c>set</c> brings the old half back, and the row loads as if the field had none.</para>
        /// </remarks>
        KeepsUnnamedHalf = 1,

        /// <summary>
        /// The list names every member, granted or denied, and a text <c>set</c> changes only the members it names, keeping
        /// the rest; an update therefore sends every member it leaves out as denied (a user group's <c>policy</c>).
        /// </summary>
        /// <remarks>
        /// <para>Measured on 6.49.21 and 7.x: <c>set policy=ftp</c> grants ftp and keeps every other policy
        /// (<c>Docs/findings-value-lists.md</c> §3).</para>
        /// <para>So the value is the whole set: an update sends the new value with every member the row's loaded value had
        /// and the new one does not name added as denied (<c>read,write</c> over a loaded <c>read,write,ftp,!local</c> is
        /// sent as <c>read,write,!ftp,!local</c>), on every transport. A value naming every member is exact on the router,
        /// and so is an add, which denies every member it does not name.</para>
        /// <para>An update of a row that was never loaded is refused before anything is sent: without the loaded value
        /// there is no telling which members the router holds. Load the row (<c>LoadById</c>, <c>LoadList</c>, a merge)
        /// and change its value.</para>
        /// </remarks>
        KeepsUnnamedMembers = 2,
    }
}
