using System;

namespace tik4net.Objects
{
    /// <summary>
    /// Marks the enum member a mapped property reads as when the router prints a word the enum does not know.
    /// </summary>
    /// <remarks>
    /// <para>
    /// RouterOS adds words to a field's vocabulary in new versions and uses old ones on old versions. Without
    /// this member an unknown word fails the read of the <b>whole</b> menu (every row, every property). With
    /// it, the property reads as this member and the router's word is kept: see
    /// <see cref="TikEntityObjectsExtensions.GetUnknownWord{TEntity}(TEntity, string)"/>.
    /// </para>
    /// <para>
    /// Writing stays strict. An entity read with an unknown word writes that word back unchanged (a save only
    /// sends it if the caller asks for a full update); the member assigned by the caller, with no word read
    /// behind it, cannot be sent and throws. The member carries no <see cref="TikEnumAttribute"/> — it is not a
    /// word the router accepts. Every enum a built-in entity maps declares one, as
    /// <c>[TikEnumUnknown] Unknown = -1</c> (a high bit on a <c>[Flags]</c> enum).
    /// </para>
    /// </remarks>
    [AttributeUsage(AttributeTargets.Field)]
    public sealed class TikEnumUnknownAttribute : Attribute
    {
    }
}
