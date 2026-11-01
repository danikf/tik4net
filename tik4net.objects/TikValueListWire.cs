using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;

namespace tik4net.Objects
{
    /// <summary>
    /// The text form of a <see cref="TikValueList{T}"/> field — the same on the API, REST and the CLI (measured on
    /// 6.49.13 and 7.24.5): items separated by <c>,</c>; on a list of <see cref="TikNegatableValue{T}"/> an item's own
    /// <c>!</c> in front of it, which the item's converter reads and writes. The whole-list <c>!</c> is handled by the
    /// property, not here.
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
        internal static TikValueList<TItem> Parse<TItem>(string text, TikWireConverter item)
        {
            if (text.Length == 0)
                return TikValueList<TItem>.Empty;
            var items = new List<TikValue<TItem>>();
            foreach (string element in text.Split(','))
                items.Add(ParseItem<TItem>(element, item));
            return new TikValueList<TItem>(items);
        }

        private static TikValue<TItem> ParseItem<TItem>(string word, TikWireConverter item)
            => TryParseItem(word, item, out TItem value, out _) ? value : TikValue<TItem>.FromWire(word);

        /// <summary>
        /// The item <paramref name="word"/> spells. False when the type cannot hold it, with the reason when there is one:
        /// the empty word on a type with no empty value, a bare <c>!</c> on a list whose members carry their own (the
        /// router's whole-list negation, not a member), a word the type does not read.
        /// </summary>
        internal static bool TryParseItem<TItem>(string word, TikWireConverter item, out TItem value, out FormatException? failure)
        {
            value = default!;
            failure = null;
            if (word == TikNegation.Bang && TikNegatableValue.IsNegatableValue(typeof(TItem)))
                return false;
            object? parsed;
            try
            {
                parsed = item.ConvertFromString(word);
            }
            catch (FormatException e)
            {
                failure = e;
                return false;
            }
            if (parsed == null)
                return false;
            value = (TItem)parsed;
            return true;
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
                if (element.Length == 0 || element == TikNegation.Bang)
                    continue;
                if (TikNegation.TrySplit(element, out _))
                    negated = true;
                else
                    plain = true;
            }
            return (plain, negated);
        }

        /// <summary>
        /// The items in the router's spelling, comma-separated, in the order the list holds them.
        /// </summary>
        internal static string Format<TItem>(TikValueList<TItem> list, TikWireConverter item)
            => string.Join(",", list.Select(v => v.IsWord ? v.RawValue! : item.ConvertToString(v.Value) ?? ""));
    }
}
