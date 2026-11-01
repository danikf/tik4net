#if NET8_0_OR_GREATER
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace tik4net.Objects
{
    /// <summary>
    /// System.Text.Json support for <see cref="TikValueList{T}"/>, applied to the type itself — no registration needed.
    /// </summary>
    /// <remarks>
    /// A JSON array of items, each in the router's spelling (<c>"22"</c>, <c>"1000-2000"</c>, <c>"from-client"</c>, and on a
    /// list of <see cref="TikNegatableValue{T}"/> a negated member with its <c>!</c>: <c>"!ack"</c>); a word the type cannot
    /// hold is <c>{"$raw":"word"}</c>, the convention of <see cref="TikField{T}"/>'s converter.
    /// </remarks>
    public sealed class TikValueListJsonConverterFactory : JsonConverterFactory
    {
        /// <inheritdoc/>
        public override bool CanConvert(Type typeToConvert)
            => typeToConvert.IsGenericType && typeToConvert.GetGenericTypeDefinition() == typeof(TikValueList<>);

        /// <inheritdoc/>
        [UnconditionalSuppressMessage("Trimming", "IL2026", Justification = "TikValueList<T> is populated by the O/R mapper, which carries the same warning.")]
        [UnconditionalSuppressMessage("Trimming", "IL2055", Justification = "TikValueList<T> is populated by the O/R mapper, which carries the same warning.")]
        [UnconditionalSuppressMessage("Trimming", "IL2070", Justification = "TikValueList<T> is populated by the O/R mapper, which carries the same warning.")]
        [UnconditionalSuppressMessage("AOT", "IL3050", Justification = "tik4net.objects is not AOT-compatible; see its project file.")]
        public override JsonConverter? CreateConverter(Type typeToConvert, JsonSerializerOptions options)
            => (JsonConverter?)Activator.CreateInstance(
                typeof(TikValueListJsonConverter<>).MakeGenericType(typeToConvert.GetGenericArguments()[0]));
    }

    [UnconditionalSuppressMessage("Trimming", "IL2026", Justification = "TikValueList<T> is populated by the O/R mapper, which carries the same warning.")]
    [UnconditionalSuppressMessage("AOT", "IL3050", Justification = "tik4net.objects is not AOT-compatible; see its project file.")]
    internal sealed class TikValueListJsonConverter<T> : JsonConverter<TikValueList<T>>
    {
        private static readonly TikWireConverter Item = new TikWireConverter(typeof(T), "TikValueList<" + typeof(T).Name + ">", "json");

        public override TikValueList<T> Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            if (reader.TokenType != JsonTokenType.StartArray)
                throw new JsonException("A TikValueList is a JSON array.");
            var items = new List<TikValue<T>>();
            while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)
                items.Add(ReadItem(ref reader, options));
            return new TikValueList<T>(items);
        }

        private static TikValue<T> ReadItem(ref Utf8JsonReader reader, JsonSerializerOptions options)
        {
            if (reader.TokenType == JsonTokenType.String)
            {
                // As a load reads the same item: a value, or the word when the type cannot hold it.
                string word = reader.GetString()!;
                return TikValueListWire.TryParseItem(word, Item, out T value, out _) ? value : TikValue<T>.FromWire(word);
            }
            if (reader.TokenType != JsonTokenType.StartObject)
                throw new JsonException("A TikValueList item is a string in the router's spelling or {\"" + TikFieldJson<T>.RawProperty + "\":\"word\"}.");

            var item = TikFieldJson<T>.Read(ref reader, options);
            if (item.State != TikFieldState.Unparsed)
                throw new JsonException("A TikValueList item object must carry \"" + TikFieldJson<T>.RawProperty + "\".");
            return item.Content;
        }

        public override void Write(Utf8JsonWriter writer, TikValueList<T> value, JsonSerializerOptions options)
        {
            writer.WriteStartArray();
            foreach (var item in value)
                WriteItem(writer, item);
            writer.WriteEndArray();
        }

        private static void WriteItem(Utf8JsonWriter writer, TikValue<T> item)
        {
            if (item.IsWord)
            {
                writer.WriteStartObject();
                writer.WriteString(TikFieldJson<T>.RawProperty, item.RawValue);
                writer.WriteEndObject();
            }
            else
                writer.WriteStringValue(Item.ConvertToString(item.Value));
        }
    }
}
#endif
