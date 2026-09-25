#if NET8_0_OR_GREATER
using System;
using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace tik4net.Objects
{
    /// <summary>
    /// System.Text.Json support for <see cref="TikValue{T}"/>, applied to the type itself — no registration needed.
    /// </summary>
    /// <remarks>
    /// <list type="table">
    /// <item><term>Present</term><description>the value itself — an enum as the word the router prints (<c>"auto"</c>),
    /// the mapper's value types (TikDuration, TikDataRate, TikRatePair, MacAddress) as the router spells them, anything else as <see cref="JsonSerializer"/> writes it with the caller's options.</description></item>
    /// <item><term>Absent</term><description><c>null</c>.</description></item>
    /// <item><term>Unparsed</term><description><c>{"$raw":"word"}</c>, read back as the same Unparsed value.</description></item>
    /// </list>
    /// <c>null</c> reads back Absent. A <c>null</c> assigned as an intent to unset therefore does not survive the round
    /// trip — deliberately the safe direction: a deserialized entity never unsets a field on the strength of JSON.
    /// Without this converter a <see cref="TikValue{T}"/> serialized its properties and deserialized as Absent, silently.
    /// </remarks>
    public sealed class TikValueJsonConverterFactory : JsonConverterFactory
    {
        /// <inheritdoc/>
        public override bool CanConvert(Type typeToConvert)
            => typeToConvert.IsGenericType && typeToConvert.GetGenericTypeDefinition() == typeof(TikValue<>);

        /// <inheritdoc/>
        [UnconditionalSuppressMessage("Trimming", "IL2026", Justification = "TikValue<T> is populated by the O/R mapper, which carries the same warning.")]
        [UnconditionalSuppressMessage("Trimming", "IL2055", Justification = "TikValue<T> is populated by the O/R mapper, which carries the same warning.")]
        [UnconditionalSuppressMessage("Trimming", "IL2070", Justification = "TikValue<T> is populated by the O/R mapper, which carries the same warning.")]
        [UnconditionalSuppressMessage("AOT", "IL3050", Justification = "tik4net.objects is not AOT-compatible; see its project file.")]
        public override JsonConverter? CreateConverter(Type typeToConvert, JsonSerializerOptions options)
            => (JsonConverter?)Activator.CreateInstance(
                typeof(TikValueJsonConverter<>).MakeGenericType(typeToConvert.GetGenericArguments()[0]));
    }

    [UnconditionalSuppressMessage("Trimming", "IL2026", Justification = "TikValue<T> is populated by the O/R mapper, which carries the same warning.")]
    [UnconditionalSuppressMessage("AOT", "IL3050", Justification = "tik4net.objects is not AOT-compatible; see its project file.")]
    internal sealed class TikValueJsonConverter<T> : JsonConverter<TikValue<T>>
    {
        private const string RawProperty = "$raw";
        private static readonly Type ValueType = Nullable.GetUnderlyingType(typeof(T)) ?? typeof(T);

        public override bool HandleNull => true;

        public override TikValue<T> Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            if (reader.TokenType == JsonTokenType.Null)
                return default;

            if (reader.TokenType == JsonTokenType.StartObject)
            {
                string? raw = null;
                while (reader.Read() && reader.TokenType != JsonTokenType.EndObject)
                {
                    string? name = reader.GetString();
                    reader.Read();
                    if (name == RawProperty)
                        raw = reader.GetString();
                    else
                        reader.Skip();
                }
                if (raw == null)
                    throw new JsonException("A TikValue object must carry \"" + RawProperty + "\".");
                return TikValue<T>.FromWire(raw);
            }

            if (ValueType.IsEnum && reader.TokenType == JsonTokenType.String)
                return ReadEnum(reader.GetString()!);
            if (IsWireStringType && reader.TokenType == JsonTokenType.String)
                return (T)ParseWireString(reader.GetString()!);

            return JsonSerializer.Deserialize<T>(ref reader, options)!;
        }

        public override void Write(Utf8JsonWriter writer, TikValue<T> value, JsonSerializerOptions options)
        {
            switch (value.State)
            {
                case TikValueState.Absent:
                    writer.WriteNullValue();
                    return;
                case TikValueState.Unparsed:
                    writer.WriteStartObject();
                    writer.WriteString(RawProperty, value.RawValue);
                    writer.WriteEndObject();
                    return;
            }

            T inner = value.Value;
            if (inner == null)
                writer.WriteNullValue();
            else if (ValueType.IsEnum || IsWireStringType)
                writer.WriteStringValue(value.ToString());   // the router's word / spelling
            else
                JsonSerializer.Serialize(writer, inner, options);
        }

        // The mapper's own value types travel as the string the router prints (10s, 1M, 10M/5M, AA:BB:…), which each
        // parses back; written as JSON objects they would collide with the {"$raw":…} form.
        private static bool IsWireStringType
            => ValueType == typeof(TikDuration) || ValueType == typeof(TikDataRate)
               || ValueType == typeof(TikRatePair) || ValueType == typeof(MacAddress);

        private static object ParseWireString(string text)
            => ValueType == typeof(TikDuration) ? TikDuration.Parse(text)
             : ValueType == typeof(TikDataRate) ? TikDataRate.Parse(text)
             : ValueType == typeof(TikRatePair) ? TikRatePair.Parse(text)
             : (object)new MacAddress(text);

        private static TikValue<T> ReadEnum(string word)
        {
            var metadata = TikEnumMetadata.Get(ValueType);
            long result = 0;
            foreach (string part in metadata.IsFlags ? word.Split(',') : new[] { word })
            {
                if (!metadata.TryParseNumeric(part.Trim(), out long numeric))
                    return TikValue<T>.FromWire(word);
                result |= numeric;
            }
            return (T)Enum.ToObject(ValueType, result);
        }
    }
}
#endif
