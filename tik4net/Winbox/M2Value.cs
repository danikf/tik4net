using System.Collections;
using System.Collections.Generic;

namespace tik4net.Winbox
{
    /// <summary>
    /// One decoded M2 field value: its <see cref="M2Type"/> and the value <see cref="M2Message.ParseAllFields"/> read.
    /// </summary>
    /// <remarks>
    /// Values by type: bool → <see cref="bool"/>; u32 → <see cref="uint"/>, or <see cref="byte"/> in the short form;
    /// u64 → <see cref="ulong"/>; addr6 → 16 <see cref="byte"/>s; string → <see cref="string"/>; raw → hex text;
    /// message → <see cref="M2Record"/>; message[] → a list of them; any other array → the text <c>"[a,b,…]"</c>.
    /// <c>null</c> when the field's declared length ran past the end of a truncated frame.
    /// </remarks>
    internal readonly struct M2Value
    {
        internal M2Value(M2Type type, object? value, bool shortForm = false)
        {
            Type = type;
            Value = value;
            ShortForm = shortForm;
        }

        internal M2Type Type { get; }

        internal object? Value { get; }

        /// <summary>
        /// The type byte carried the short size flag. Not part of the type — a u8 is a u32 — and kept only so the
        /// trace (<see cref="M2Message.Describe"/>) names the form the router sent: <c>u8</c>, <c>str</c> and
        /// <c>raw</c> against <c>u32</c>, <c>str_l</c> and <c>raw_l</c>.
        /// </summary>
        internal bool ShortForm { get; }

        /// <summary>The trace's name for this value's type and form (<c>u8</c>, <c>str_l</c>, <c>msg[]</c>, …).</summary>
        internal string TraceName => M2Types.TraceName(Type, ShortForm);
    }

    /// <summary>
    /// One decoded M2 message: its fields by full key. A key the message carries twice with two kinds of value (a
    /// scalar and an array, a bool and a number) holds the second under its qualified key
    /// (<see cref="WinboxM2Protocol.TypedKey"/>) — see <see cref="M2Message.ParseAllFields"/>.
    /// </summary>
    internal sealed class M2Record : IReadOnlyDictionary<int, M2Value>
    {
        private readonly Dictionary<int, M2Value> _fields = new Dictionary<int, M2Value>();

        public M2Value this[int key]
        {
            get => _fields[key];
            internal set => _fields[key] = value;
        }

        public int Count => _fields.Count;

        public IEnumerable<int> Keys => _fields.Keys;

        public IEnumerable<M2Value> Values => _fields.Values;

        public bool ContainsKey(int key) => _fields.ContainsKey(key);

        public bool TryGetValue(int key, out M2Value value) => _fields.TryGetValue(key, out value);

        /// <summary>
        /// True when the message carries a value of <paramref name="type"/>'s kind under <paramref name="wireKey"/>'s
        /// qualified key — the second of two kinds of value on one key.
        /// </summary>
        internal bool ContainsQualified(int wireKey, M2Type type)
            => _fields.ContainsKey(WinboxM2Protocol.TypedKey.Qualify(wireKey, type));

        public IEnumerator<KeyValuePair<int, M2Value>> GetEnumerator() => _fields.GetEnumerator();

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }
}
