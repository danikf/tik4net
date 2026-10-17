#if NET8_0_OR_GREATER
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace tik4net.Objects
{
    /// <summary>
    /// System.Text.Json support for <see cref="TikField{T}"/> and <see cref="TikNegatableField{T}"/>, applied to the types
    /// themselves — no registration needed.
    /// </summary>
    /// <remarks>
    /// <list type="table">
    /// <item><term>Present</term><description>the value itself — an enum as the word the router prints (<c>"auto"</c>),
    /// the mapper's value types (TikDuration, TikDataRate, TikRatePair, TikHexNumber, MacAddress) as the router spells them, anything else as <see cref="JsonSerializer"/> writes it with the caller's options.</description></item>
    /// <item><term>Absent</term><description><c>null</c>.</description></item>
    /// <item><term>Unparsed</term><description><c>{"$raw":"word"}</c>, read back as the same Unparsed value.</description></item>
    /// <item><term>Negated</term><description><c>{"$not":value}</c>, the value as above (<see cref="TikNegatableField{T}.IsNegated"/>);
    /// a <see cref="TikField{T}"/> refuses it with a <see cref="JsonException"/>, as it has nowhere to keep the negation.</description></item>
    /// </list>
    /// A string value is written as it is, so a <see cref="TikNegatableField{T}"/>'s negated <c>"!x"</c> travels as
    /// <c>{"$not":"x"}</c>, never as a <c>"!x"</c> that a <see cref="TikField{T}"/> would read as text.
    /// <c>null</c> reads back Absent. A <c>null</c> assigned as an intent to unset therefore does not survive the round
    /// trip — deliberately the safe direction: a deserialized entity never unsets a field on the strength of JSON.
    /// Without this converter a field serialized its properties and deserialized as Absent, silently.
    /// </remarks>
    public sealed class TikFieldJsonConverterFactory : JsonConverterFactory
    {
        /// <inheritdoc/>
        public override bool CanConvert(Type typeToConvert)
            => typeToConvert.IsGenericType
               && (typeToConvert.GetGenericTypeDefinition() == typeof(TikField<>)
                   || typeToConvert.GetGenericTypeDefinition() == typeof(TikNegatableField<>));

        /// <inheritdoc/>
        [UnconditionalSuppressMessage("Trimming", "IL2026", Justification = "TikField<T> is populated by the O/R mapper, which carries the same warning.")]
        [UnconditionalSuppressMessage("Trimming", "IL2055", Justification = "TikField<T> is populated by the O/R mapper, which carries the same warning.")]
        [UnconditionalSuppressMessage("Trimming", "IL2070", Justification = "TikField<T> is populated by the O/R mapper, which carries the same warning.")]
        [UnconditionalSuppressMessage("AOT", "IL3050", Justification = "tik4net.objects is not AOT-compatible; see its project file.")]
        public override JsonConverter? CreateConverter(Type typeToConvert, JsonSerializerOptions options)
            => (JsonConverter?)Activator.CreateInstance(
                (typeToConvert.GetGenericTypeDefinition() == typeof(TikField<>) ? typeof(TikFieldJsonConverter<>) : typeof(TikNegatableFieldJsonConverter<>))
                    .MakeGenericType(typeToConvert.GetGenericArguments()[0]));
    }

    [UnconditionalSuppressMessage("Trimming", "IL2026", Justification = "TikField<T> is populated by the O/R mapper, which carries the same warning.")]
    [UnconditionalSuppressMessage("AOT", "IL3050", Justification = "tik4net.objects is not AOT-compatible; see its project file.")]
    internal sealed class TikFieldJsonConverter<T> : JsonConverter<TikField<T>>
    {
        public override bool HandleNull => true;

        public override TikField<T> Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            var core = TikFieldJson<T>.Read(ref reader, options);
            if (core.Negated)
                throw new JsonException("\"" + TikFieldJson<T>.NotProperty + "\" is a negation, and a TikField<" + typeof(T).Name
                    + "> does not take one; a negatable field is a TikNegatableField<T>.");
            return !core.Printed ? default : core.Content.IsWord ? TikField<T>.FromUnparsed(core.Content.RawValue!) : TikField<T>.FromPresent(core.Content.Value);
        }

        public override void Write(Utf8JsonWriter writer, TikField<T> value, JsonSerializerOptions options)
            => TikFieldJson<T>.Write(writer, value.Core, options);
    }

    [UnconditionalSuppressMessage("Trimming", "IL2026", Justification = "TikField<T> is populated by the O/R mapper, which carries the same warning.")]
    [UnconditionalSuppressMessage("AOT", "IL3050", Justification = "tik4net.objects is not AOT-compatible; see its project file.")]
    internal sealed class TikNegatableFieldJsonConverter<T> : JsonConverter<TikNegatableField<T>>
    {
        public override bool HandleNull => true;

        public override TikNegatableField<T> Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            var core = TikFieldJson<T>.Read(ref reader, options);
            // A plain value goes through the assignment conversion, so a hand-written "!x" reads as the negation it spells.
            return core.State == TikFieldState.Present && !core.Negated ? core.Content.Value : new TikNegatableField<T>(core);
        }

        public override void Write(Utf8JsonWriter writer, TikNegatableField<T> value, JsonSerializerOptions options)
            => TikFieldJson<T>.Write(writer, value.Core, options);
    }

    /// <summary>The JSON form both field types share; see <see cref="TikFieldJsonConverterFactory"/>.</summary>
    [UnconditionalSuppressMessage("Trimming", "IL2026", Justification = "TikField<T> is populated by the O/R mapper, which carries the same warning.")]
    [UnconditionalSuppressMessage("AOT", "IL3050", Justification = "tik4net.objects is not AOT-compatible; see its project file.")]
    internal static class TikFieldJson<T>
    {
        internal const string RawProperty = "$raw";
        internal const string NotProperty = "$not";
        private static readonly Type ValueType = Nullable.GetUnderlyingType(typeof(T)) ?? typeof(T);

        internal static TikFieldCore<T> Read(ref Utf8JsonReader reader, JsonSerializerOptions options)
        {
            if (reader.TokenType == JsonTokenType.Null)
                return default;

            if (reader.TokenType == JsonTokenType.StartObject)
            {
                string? raw = null;
                TikFieldCore<T>? negated = null;
                while (reader.Read() && reader.TokenType != JsonTokenType.EndObject)
                {
                    string? name = reader.GetString();
                    reader.Read();
                    if (name == RawProperty)
                        raw = reader.GetString();
                    else if (name == NotProperty)
                    {
                        var inner = Read(ref reader, options);
                        negated = inner.State == TikFieldState.Present && inner.Content.Value != null
                            ? new TikFieldCore<T>(inner.Content, negated: true)
                            : inner;
                    }
                    else
                        reader.Skip();
                }
                if (negated != null)
                    return negated.Value;
                if (raw == null)
                    throw new JsonException("A TikField object must carry \"" + RawProperty + "\" or \"" + NotProperty + "\".");
                return new TikFieldCore<T>(TikValue<T>.FromWire(raw));
            }

            if (ValueType.IsEnum && reader.TokenType == JsonTokenType.String)
                return ReadEnum(reader.GetString()!);
            if (IsWireStringType && reader.TokenType == JsonTokenType.String)
                return new TikFieldCore<T>((T)ParseWireString(reader.GetString()!));

            return new TikFieldCore<T>(JsonSerializer.Deserialize<T>(ref reader, options)!);
        }

        internal static void Write(Utf8JsonWriter writer, TikFieldCore<T> value, JsonSerializerOptions options)
        {
            switch (value.State)
            {
                case TikFieldState.Absent:
                    writer.WriteNullValue();
                    return;
                case TikFieldState.Unparsed:
                    writer.WriteStartObject();
                    writer.WriteString(RawProperty, value.RawValue);
                    writer.WriteEndObject();
                    return;
            }

            if (value.IsNegated)
            {
                writer.WriteStartObject();
                writer.WritePropertyName(NotProperty);
                Write(writer, value.WithoutNegation(), options);
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
               || ValueType == typeof(TikRatePair) || ValueType == typeof(TikHexNumber) || ValueType == typeof(MacAddress);

        private static object ParseWireString(string text)
            => ValueType == typeof(TikDuration) ? TikDuration.Parse(text)
             : ValueType == typeof(TikDataRate) ? TikDataRate.Parse(text)
             : ValueType == typeof(TikRatePair) ? TikRatePair.Parse(text)
             : ValueType == typeof(TikHexNumber) ? TikHexNumber.Parse(text)
             : (object)new MacAddress(text);

        private static TikFieldCore<T> ReadEnum(string word)
        {
            var metadata = TikEnumMetadata.Get(ValueType);
            return metadata.TryParseNumeric(word, out long single)
                ? new TikFieldCore<T>((T)Enum.ToObject(ValueType, single))
                : new TikFieldCore<T>(TikValue<T>.FromWire(word));
        }
    }
}
#endif
