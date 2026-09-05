using System;

namespace tik4net.Objects
{
    /// <summary>
    /// The write verbs a RouterOS menu offers, as declared by
    /// <see cref="TikEntityAttribute.SupportedOperations"/> and enforced by the mapper before it builds a
    /// command.
    /// </summary>
    /// <remarks>
    /// <para>
    /// RouterOS decides this per verb and per menu, not per menu alone: <c>/ppp/active</c> has
    /// <c>remove</c> and no <c>add</c>/<c>set</c>, <c>/routing/ospf/neighbor</c> has <c>set</c> and no
    /// <c>add</c>/<c>remove</c>. A single "the entity is read-only" flag cannot express either, which is
    /// why one is no longer the model — see
    /// <see href="https://github.com/danikf/tik4net/issues/84">issue #84</see>.
    /// </para>
    /// <para>
    /// The polarity is positive — a value states what the menu <b>does</b> offer — matching
    /// <see cref="TikConnectionCapability"/>. <see cref="All"/> is therefore the default that every
    /// <see cref="TikEntityAttribute"/> constructor seeds, and an entity narrows it.
    /// </para>
    /// <para>
    /// This is about the menu's <i>verbs</i>. Whether an individual field may be written is separate and
    /// stays on <see cref="TikPropertyAttribute.IsReadOnly"/>; whether a menu has an order at all stays on
    /// <see cref="TikEntityAttribute.IsOrdered"/>, which <see cref="Move"/> does not replace.
    /// </para>
    /// </remarks>
    /// <seealso cref="TikEntityAttribute.SupportedOperations"/>
    /// <seealso cref="TikEntityMetadata.Supports"/>
    [Flags]
    public enum TikEntityOperations
    {
        /// <summary>The menu offers no write verb at all — it can only be read (<c>/log</c>, <c>/ip/neighbor</c>, the monitor/action entities).</summary>
        None = 0,

        /// <summary>The menu offers <c>add</c>, so <see cref="TikConnectionExtensions.Save">Save</see> may create a row.</summary>
        Add = 1,

        /// <summary>The menu offers <c>set</c> (and <c>unset</c>), so <see cref="TikConnectionExtensions.Save">Save</see> may update a row.</summary>
        Set = 2,

        /// <summary>The menu offers <c>remove</c>, so <see cref="TikConnectionExtensions.Delete">Delete</see> may drop a row — true for several menus whose fields are read-only, e.g. <c>/ppp/active</c>.</summary>
        Remove = 4,

        /// <summary>The menu offers <c>move</c>, so <see cref="TikConnectionExtensions.Move">Move</see> may reorder rows. Only meaningful together with <see cref="TikEntityAttribute.IsOrdered"/>.</summary>
        Move = 8,

        /// <summary>All four verbs — the default for a normal configuration menu.</summary>
        All = Add | Set | Remove | Move,
    }
}
