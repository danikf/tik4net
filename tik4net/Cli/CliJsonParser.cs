using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Text.Json;
using tik4net.Connection;
using tik4net.Diagnostics;

namespace tik4net.Cli
{
    /// <summary>
    /// Parses the output of a <c>:put [:serialize to=json [/path print … as-value]]</c> command into
    /// <see cref="TikRecordSentence"/> records.
    ///
    /// <para>WHY this exists next to <see cref="CliOutputParser"/>: RouterOS's <c>as-value</c> format has
    /// no escaping at all. Records are joined with <c>;</c>, fields with <c>;</c> and <c>=</c>, and a value
    /// that itself contains those characters — a file body (<c>/file contents</c>), a script source — is
    /// therefore indistinguishable from further fields and records. JSON is escaped, so the same read is
    /// exact.
    /// <br/>
    /// Measured on 7.23.2, reading <c>/file</c> over telnet WITHOUT this path: the record count survives
    /// (records still split at <c>.id=</c>), but every body is corrupted — <c>CliOutputParser</c> turns the
    /// newlines inside <c>contents</c> into field separators and then re-joins the pieces as multi-value
    /// elements, so an XML file comes back with its line breaks replaced by commas. The result set looks
    /// full and is quietly wrong, which is worse than a short read: nothing about it invites suspicion.</para>
    ///
    /// <para>Values are converted to the string wire form the binary API would have produced, so both
    /// parsers feed the O/R mapper identically:
    /// <list type="bullet">
    ///   <item><description>string → unchanged; <c>null</c> → empty string</description></item>
    ///   <item><description><c>true</c>/<c>false</c> → <c>"true"</c>/<c>"false"</c></description></item>
    ///   <item><description>array → elements joined with <c>,</c> (the API's multi-value separator) —
    ///     e.g. <c>"key-usage":["key-cert-sign","crl-sign"]</c> → <c>key-cert-sign,crl-sign</c></description></item>
    ///   <item><description>number → integral values are emitted WITHOUT a fractional part. RouterOS
    ///     renders some numeric fields as floats (measured: <c>"key-size":2048.000000</c>,
    ///     <c>"factory-software":7.100000</c>); passing that through would break the mapper's
    ///     <c>int</c>/<c>long</c> conversion on a value the API reports as a plain integer.</description></item>
    /// </list>
    /// Anything that cannot be mapped — a nested object, an array of arrays — <b>throws</b> rather than
    /// degrading to a plausible-looking string: a wrong value that parses is far worse than a loud
    /// failure.</para>
    /// </summary>
    internal static class CliJsonParser
    {
        /// <summary>
        /// Parses <paramref name="output"/> into a list of re-sentences. Empty output → empty list
        /// (RouterOS answers an empty result set with <c>[]</c>, but stay defensive).
        /// A top-level object rather than an array is a singleton menu (measured: <c>/system resource</c>
        /// serialises as a bare <c>{…}</c>) and yields exactly one record.
        /// </summary>
        internal static IList<TikRecordSentence> ParseJson(string output)
        {
            var result = new List<TikRecordSentence>();
            if (string.IsNullOrWhiteSpace(output))
                return result;

            output = StripRawControlCharacters(output);

            JsonDocument doc;
            try
            {
                doc = JsonDocument.Parse(output);
            }
            catch (JsonException ex)
            {
                throw new TikSentenceException(
                    "The router's ':serialize to=json' output is not valid JSON: " + ex.Message
                        + " Response was: " + Excerpt(output), null!); // ctor's sentence param is optional/nullable by contract (out of scope here)
            }

            using (doc)
            {
                var root = doc.RootElement;
                switch (root.ValueKind)
                {
                    case JsonValueKind.Array:
                        foreach (var element in root.EnumerateArray())
                        {
                            if (element.ValueKind != JsonValueKind.Object)
                                throw new TikSentenceException(
                                    "Expected a JSON object per record, got " + element.ValueKind
                                        + ". Response was: " + Excerpt(output), null!); // see comment above
                            result.Add(ReadRecord(element));
                        }
                        break;
                    case JsonValueKind.Object:
                        result.Add(ReadRecord(root));
                        break;
                    default:
                        throw new TikSentenceException(
                            "Expected a JSON array or object, got " + root.ValueKind
                                + ". Response was: " + Excerpt(output), null!); // see comment above
                }
            }

            return result;
        }

        private static TikRecordSentence ReadRecord(JsonElement element)
        {
            var fields = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var property in element.EnumerateObject())
                // Through the same normaliser as the as-value path: ':serialize to=json' fixes the
                // FRAMING, not the spelling — it still renders a duration as 00:00:00 and a sentinel as a
                // bare number, exactly as as-value does. Skipping it here made a path read differently
                // depending only on whether one of its fields happened to be marked free-text.
                fields[property.Name] = CliValueNormalizer.Normalize(
                    property.Name, ReadValue(property.Value, property.Name), fromJson: true);
            return new TikRecordSentence(fields);
        }

        private static string ReadValue(JsonElement value, string fieldName)
        {
            switch (value.ValueKind)
            {
                case JsonValueKind.String:
                    return value.GetString()!; // ValueKind.String guarantees a non-null string here
                case JsonValueKind.True:
                    return "true";
                case JsonValueKind.False:
                    return "false";
                case JsonValueKind.Null:
                    return string.Empty;
                case JsonValueKind.Number:
                    return NormalizeNumber(value.GetRawText());
                case JsonValueKind.Array:
                    return JoinArray(value, fieldName);
                default:
                    // A nested object has never been observed in as-value output. Refuse it loudly rather
                    // than handing the caller raw JSON text that looks like a value (P2.25).
                    throw new TikSentenceException(
                        "Field '" + fieldName + "' holds an unsupported JSON " + value.ValueKind
                            + " value: " + Excerpt(value.GetRawText()), null!); // see comment above
            }
        }

        private static string JoinArray(JsonElement array, string fieldName)
        {
            var sb = new StringBuilder();
            foreach (var element in array.EnumerateArray())
            {
                if (element.ValueKind == JsonValueKind.Array || element.ValueKind == JsonValueKind.Object)
                    throw new TikSentenceException(
                        "Field '" + fieldName + "' holds a nested " + element.ValueKind
                            + ", which has no equivalent in the binary API's flat multi-value form: "
                            + Excerpt(array.GetRawText()), null!); // see comment above

                if (sb.Length > 0)
                    sb.Append(',');
                sb.Append(ReadValue(element, fieldName));
            }
            return sb.ToString();
        }

        /// <summary>
        /// Renders a JSON number the way the binary API reports the same field. RouterOS serialises some
        /// integer fields with a fractional part (measured on 7.23.2: <c>key-size</c> as
        /// <c>2048.000000</c>); an integral value is therefore emitted without one, so
        /// <c>int</c>/<c>long</c> properties convert. A genuinely fractional value keeps its digits, minus
        /// trailing zeros. Anything <c>decimal</c> cannot hold (very large or exponential) is passed
        /// through verbatim — the mapper's own conversion error is then the honest outcome.
        /// </summary>
        internal static string NormalizeNumber(string rawText)
        {
            if (rawText.IndexOf('.') < 0 && rawText.IndexOf('e') < 0 && rawText.IndexOf('E') < 0)
                return rawText;

            decimal parsed;
            if (!decimal.TryParse(rawText, NumberStyles.Float, CultureInfo.InvariantCulture, out parsed))
                return rawText;

            decimal truncated = decimal.Truncate(parsed);
            if (parsed == truncated)
                return truncated.ToString(CultureInfo.InvariantCulture);

            // 'G29' drops the trailing zeros decimal keeps from the literal's scale (7.100000 → 7.1).
            return parsed.ToString("G29", CultureInfo.InvariantCulture);
        }

        /// <summary>
        /// Removes raw control characters (&lt; 0x20) from the response.
        /// <para>
        /// JSON forbids them inside a string — <c>:serialize</c> escapes what it can, so a real newline in
        /// a file body arrives as the two characters <c>\n</c> and is untouched here. A RAW one can
        /// therefore only come from something that is not the value:
        /// <list type="bullet">
        ///   <item><description>the terminal wrapping a very long line. Measured on 7.23.2: a 30 KB
        ///     <c>/file</c> read over <c>winboxcli</c> arrives with a raw <c>0x0A</c> at offset 10001 —
        ///     mid-word, inside a CSS body — while telnet delivers the same read unbroken.</description></item>
        ///   <item><description>a BINARY file body. RouterOS serialises <c>/file contents</c> for a .gif the
        ///     same way it does for text, and those bytes cannot survive a text terminal at all: measured,
        ///     two GIFs lost 75 and 19 characters in transit while every text file round-tripped
        ///     exactly. Their contents are mangled either way — the binary API mangles them too, by
        ///     decoding arbitrary bytes as UTF-8 — so dropping the leftovers keeps CLI at parity with the
        ///     API rather than failing the whole listing over a value nobody can use.</description></item>
        /// </list>
        /// The alternative — refusing to parse — would take out the entire <c>/file</c> listing on any
        /// router holding one image, which is most of them.
        /// </para>
        /// </summary>
        internal static string StripRawControlCharacters(string text)
        {
            int firstControl = -1;
            for (int i = 0; i < text.Length; i++)
            {
                if (text[i] < ' ')
                {
                    firstControl = i;
                    break;
                }
            }
            if (firstControl < 0)
                return text;    // the common case: nothing to do, no allocation

            var sb = new StringBuilder(text.Length);
            sb.Append(text, 0, firstControl);
            int removed = 0;
            for (int i = firstControl; i < text.Length; i++)
            {
                if (text[i] < ' ')
                    removed++;
                else
                    sb.Append(text[i]);
            }

            TikWireTrace.Emit("cli.json", TikWireDir.Note,
                "dropped " + removed + " raw control character(s) from the JSON response (first at offset "
                    + firstControl + ") — terminal line-wrap or a binary file body");
            return sb.ToString();
        }

        private static string Excerpt(string text)
        {
            const int limit = 200;
            if (string.IsNullOrEmpty(text))
                return "(empty)";
            text = text.Replace("\r", "\\r").Replace("\n", "\\n");
            return text.Length <= limit ? text : text.Substring(0, limit) + "… (" + text.Length + " chars)";
        }
    }
}
