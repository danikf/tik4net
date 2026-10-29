using System;

namespace tik4net.Winbox
{
    /// <summary>
    /// The M2 field type: webfig's ftype, the upper five bits of a TLV type byte (<c>type &gt;&gt; 3</c>), with
    /// the array forms at +16. The same number a <c>.jg</c> id's prefix letter stands for (<c>id2int</c>), so a
    /// record value and a catalog field are typed by one vocabulary.
    /// </summary>
    /// <remarks>
    /// The size flags of the type byte (<see cref="M2Message.TypeByte.Short"/>, <see cref="M2Message.TypeByte.Long"/>)
    /// are not part of the type: a u8 is a short u32, and a 300-byte string is the same type as a 3-byte one.
    /// </remarks>
    internal enum M2Type
    {
        Bool = 0,
        U32 = 1,
        U64 = 2,
        Addr6 = 3,
        String = 4,
        /// <summary>A nested message — the <c>.jg</c> <c>m</c> prefix, which webfig's <c>addr</c> compound rides on.</summary>
        Message = 5,
        Raw = 6,
        BoolArray = 16,
        U32Array = 17,
        U64Array = 18,
        Addr6Array = 19,
        StringArray = 20,
        MessageArray = 21,
        RawArray = 22,

        /// <summary>
        /// A <c>.jg</c> prefix letter that is not one of <c>id2int</c>'s fourteen. No catalog has one; a field
        /// carrying it is typed as nothing in particular — it names itself as a u32 in the unset list, encodes as
        /// text, and is a scalar.
        /// </summary>
        Unknown = 0xFF,
    }

    /// <summary>The two parses of an <see cref="M2Type"/> — from a TLV type byte and from a <c>.jg</c> prefix letter.</summary>
    internal static class M2Types
    {
        /// <summary>The type of a TLV type byte, or <c>null</c> for an ftype no webfig type covers.</summary>
        internal static M2Type? FromTypeByte(int typeByte)
        {
            var type = (M2Type)(typeByte >> 3);
            switch (type)
            {
                case M2Type.Bool: case M2Type.U32: case M2Type.U64: case M2Type.Addr6:
                case M2Type.String: case M2Type.Message: case M2Type.Raw:
                case M2Type.BoolArray: case M2Type.U32Array: case M2Type.U64Array: case M2Type.Addr6Array:
                case M2Type.StringArray: case M2Type.MessageArray: case M2Type.RawArray:
                    return type;
                default:
                    return null;
            }
        }

        /// <summary>
        /// The type a <c>.jg</c> id's prefix letter names (<c>s10006</c> → <see cref="M2Type.String"/>): webfig's
        /// <c>id2int</c>, lower case for the scalar and upper case for its array.
        /// </summary>
        /// <remarks>
        /// <para>The letters are not a convention to be inferred from examples: webfig declares the whole mapping in one
        /// literal, <c>id2int={b:0&lt;&lt;27,u:1&lt;&lt;27,q:2&lt;&lt;27,a:3&lt;&lt;27,s:4&lt;&lt;27,m:5&lt;&lt;27,r:6&lt;&lt;27,B:16&lt;&lt;27,U:17&lt;&lt;27,
        /// Q:18&lt;&lt;27,A:19&lt;&lt;27,S:20&lt;&lt;27,M:21&lt;&lt;27,R:22&lt;&lt;27}</c> — an ftype per letter. Mirrors
        /// <c>jg_analyze.py</c> PREFIX.</para>
        /// <c>id2int</c>'s fourteen letters, and no more — the 7.17 and 7.24 histograms find thirteen of them, every
        /// one here (<c>B</c> being the one no window uses). A letter MISSING from this table is worse than a
        /// mistyped one: the field cannot be typed at all. That is what <c>Q</c> (FT_U64_ARRAY) did to every
        /// <c>multibignumber</c> in the catalog. An invented mapping is worse than a missing one: a missing letter
        /// makes the field obviously unreadable, an invented one makes it read WRONG.
        /// </remarks>
        internal static bool TryFromJgPrefix(char letter, out M2Type type)
        {
            M2Type scalar;
            switch (char.ToLowerInvariant(letter))
            {
                case 'b': scalar = M2Type.Bool; break;
                case 'u': scalar = M2Type.U32; break;
                case 'q': scalar = M2Type.U64; break;
                case 'a': scalar = M2Type.Addr6; break;
                case 's': scalar = M2Type.String; break;
                case 'm': scalar = M2Type.Message; break;
                case 'r': scalar = M2Type.Raw; break;
                default: type = M2Type.Unknown; return false;
            }
            type = char.IsUpper(letter) ? scalar.ArrayOf() : scalar;
            return true;
        }

        /// <summary>True for the array forms (ftype 16 and up).</summary>
        internal static bool IsArray(this M2Type type) => type >= M2Type.BoolArray && type <= M2Type.RawArray;

        /// <summary>The array form of a scalar type.</summary>
        internal static M2Type ArrayOf(this M2Type scalar) => scalar + 16;

        /// <summary>
        /// The webfig ftype number, as the unset list names a field by it (bits 27+ of
        /// <see cref="WinboxJgField.TypedFieldId"/>); <see cref="M2Type.Unknown"/> names itself as a u32, the
        /// catalog's default id prefix.
        /// </summary>
        internal static int FType(this M2Type type) => type == M2Type.Unknown ? (int)M2Type.U32 : (int)type;

        // ── Spellings: output only; nothing compares them ─────────────────────────────────

        /// <summary>
        /// The catalog's spelling of a type (<c>"string"</c>, <c>"addr[]"</c>; <c>"?"</c> for unknown), for the messages
        /// that name a field's type.
        /// </summary>
        internal static string JgName(M2Type type)
        {
            switch (type)
            {
                case M2Type.Bool: return "bool";
                case M2Type.U32: return "u32";
                case M2Type.U64: return "u64";
                case M2Type.Addr6: return "ip6";
                case M2Type.String: return "string";
                case M2Type.Message: return "addr";
                case M2Type.Raw: return "raw";
                case M2Type.Unknown: return "?";
                default: return JgName(type - 16) + "[]";
            }
        }

        /// <summary>
        /// The trace's spelling of a value of <paramref name="type"/> sent with or without the short size flag
        /// (<see cref="M2Value.TraceName"/>).
        /// </summary>
        internal static string TraceName(M2Type type, bool shortForm)
        {
            switch (type)
            {
                case M2Type.Bool: return "bool";
                case M2Type.U32: return shortForm ? "u8" : "u32";
                case M2Type.U64: return "u64";
                case M2Type.Addr6: return "ip6";
                case M2Type.String: return shortForm ? "str" : "str_l";
                case M2Type.Message: return "msg";
                case M2Type.Raw: return shortForm ? "raw" : "raw_l";
                case M2Type.BoolArray: return "bool[]";
                case M2Type.U32Array: return "u32[]";
                case M2Type.U64Array: return "u64[]";
                case M2Type.Addr6Array: return "ip6[]";
                case M2Type.StringArray: return "str[]";
                case M2Type.MessageArray: return "msg[]";
                case M2Type.RawArray: return "raw[]";
                default: return "?";   // a record built from a catalog field of an unknown letter
            }
        }
    }
}
