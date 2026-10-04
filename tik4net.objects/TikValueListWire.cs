using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;

namespace tik4net.Objects
{
    /// <summary>
    /// The text form of a <see cref="TikValueList{T}"/> field — the same on the API, REST and the CLI (measured on
    /// 6.49.13 and 7.24.5): items separated by <c>,</c>; on a field whose members the router negates one by one
    /// (<see cref="TikPropertyAttribute.NegatableMembers"/>) an item's own <c>!</c> in front of it. The whole-list
    /// <c>!</c> is handled by the property, not here.
    /// </summary>
    [RequiresUnreferencedCode(TikTrimming.MapperMessage)]
    [RequiresDynamicCode(TikTrimming.DynamicCodeMessage)]
    internal static class TikValueListWire
    {
        /// <summary>
        /// The items of <paramref name="text"/> (without a whole-list <c>!</c>). An item <typeparamref name="TItem"/> cannot
        /// hold — a member a newer RouterOS added — is kept as the router's word, with its <c>!</c>, and the rest of the
        /// list keeps its type.
        /// </summary>
        internal static TikValueList<TItem> Parse<TItem>(string text, bool negatableMembers, TikWireConverter item)
        {
            if (text.Length == 0)
                return TikValueList<TItem>.Empty;
            var items = new List<TikValue<TItem>>();
            foreach (string element in text.Split(','))
            {
                bool negated = negatableMembers && element.Length > 1 && element[0] == '!';
                string word = negated ? element.Substring(1) : element;
                items.Add(ParseItem<TItem>(word, item).AsNegated(negated));
            }
            return new TikValueList<TItem>(items);
        }

        private static TikValue<TItem> ParseItem<TItem>(string word, TikWireConverter item)
        {
            object? value;
            string? unknownWord;
            try
            {
                value = item.ConvertFromString(word, out unknownWord);
            }
            catch (FormatException)
            {
                return TikValue<TItem>.FromWire(word);
            }
            if (unknownWord != null || value == null)
                return TikValue<TItem>.FromWire(word);
            return (TItem)value;
        }

        /// <summary>
        /// Which halves a list's text holds — plain members, negated members — ignoring the whole-list <c>!</c>
        /// (a bare leading element): <c>!,syn,!ack</c> holds both, <c>syn</c> only plain ones.
        /// </summary>
        internal static (bool Plain, bool Negated) Halves(string text)
        {
            bool plain = false, negated = false;
            foreach (string element in text.Split(','))
            {
                if (element.Length == 0 || element == "!")
                    continue;
                if (element[0] == '!')
                    negated = true;
                else
                    plain = true;
            }
            return (plain, negated);
        }

        /// <summary>
        /// The items in the router's spelling, comma-separated, in the order the list holds them. A negated item on a field
        /// that does not negate its members is refused: the router refuses it, or reads the <c>!</c> as part of a word.
        /// </summary>
        internal static string Format<TItem>(TikValueList<TItem> list, bool negatableMembers, TikWireConverter item,
            string propertyName, string fieldName)
            => string.Join(",", list.Select(v =>
            {
                if (v.IsNegated && !negatableMembers)
                    throw new InvalidOperationException(string.Format(
                        "Property '{0}({1})' holds a negated item ({2}), and RouterOS does not negate this field's members one by one "
                        + "(NegatableMembers). Negate the whole list instead, if the field takes that (Negatable).",
                        propertyName, fieldName, v));
                string text = v.IsWord ? v.RawValue! : item.ConvertToString(v.Value) ?? "";
                return v.IsNegated ? "!" + text : text;
            }));
    }
}
