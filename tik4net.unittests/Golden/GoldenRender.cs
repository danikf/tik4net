#nullable enable

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using tik4net.Winbox;

namespace tik4net.unittests.Golden
{
    /// <summary>Deterministic text forms of bytes, decoded M2 records and outcomes, for the golden files.</summary>
    internal static class GoldenRender
    {
        /// <summary>Hex for anything readable on one line; a long buffer as its length, first 32 bytes and SHA-256.</summary>
        internal static string Bytes(byte[]? b)
        {
            if (b == null) return "null";
            if (b.Length <= 400) return GoldenFile.Hex(b);
            using (var sha = SHA256.Create())
                return "len=" + b.Length + " head=" + GoldenFile.Hex(b.Take(32).ToArray()) + " sha256=" + GoldenFile.Hex(sha.ComputeHash(b));
        }

        /// <summary>A decoded M2 field dictionary, keys sorted, each value as <c>type:value</c>.</summary>
        internal static string Fields(M2Record? fields)
        {
            if (fields == null) return "null";
            return "{" + string.Join(" ", fields.OrderBy(kv => kv.Key)
                .Select(kv => "0x" + kv.Key.ToString("X", CultureInfo.InvariantCulture) + "=" + kv.Value.TraceName + ":" + Value(kv.Value.Value))) + "}";
        }

        internal static string Records(List<M2Record>? records)
            => records == null ? "null" : "[" + string.Join(",", records.Select(Fields)) + "]";

        internal static string Value(object? value)
        {
            switch (value)
            {
                case null: return "null";
                case byte[] b: return "bytes:" + Bytes(b);
                case M2Record d: return Fields(d);
                case List<M2Record> l: return Records(l);
                case IFormattable f: return f.ToString(null, CultureInfo.InvariantCulture);
                case string s: return "'" + (s.Length > 200 ? s.Substring(0, 200) + "…(" + s.Length + ")" : s) + "'";
                default: return value.ToString() ?? "";
            }
        }

        /// <summary>The value, or <c>throws Type: message</c> — what a call did is the pin, either way.</summary>
        internal static string Try(Func<string> f)
        {
            try { return f(); }
            catch (Exception ex)
            {
                if (ex is System.Reflection.TargetInvocationException tie && tie.InnerException != null) ex = tie.InnerException;
                // The runtime's own messages are localized and differ between net48 and net8.0, so they are not
                // the pin; their type (and the parameter an argument check names) is.
                if (ex is ArgumentException ae) return "throws " + ex.GetType().Name + " param=" + ae.ParamName;
                if (ex is IndexOutOfRangeException || ex is NullReferenceException || ex is InvalidCastException
                    || ex is OverflowException) return "throws " + ex.GetType().Name;
                return "throws " + ex.GetType().Name + ": " + ex.Message.Replace("\r", " ").Replace("\n", " ");
            }
        }
    }
}
