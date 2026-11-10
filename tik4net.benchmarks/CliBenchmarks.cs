#nullable enable

using System;
using System.Reflection;
using System.Text;
using BenchmarkDotNet.Attributes;

namespace tik4net.Benchmarks
{
    /// <summary>Reaches the library's internal parsers without an <c>InternalsVisibleTo</c> (the call cost is noise next to a 1000-row parse).</summary>
    internal static class Internal
    {
        private static readonly Assembly Core = typeof(ITikConnection).Assembly;
        internal static MethodInfo Method(string type, string name, params Type[] args)
            => Core.GetType(type, throwOnError: true)!.GetMethod(name, BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public, null, args, null)
               ?? throw new MissingMethodException(type, name);
    }

    /// <summary>The CLI read loop's per-poll re-strip of the whole buffer, for one 1000-row answer.</summary>
    [MemoryDiagnoser]
    public class CliBenchmarks
    {
        private string _raw = null!;
        private Func<string, string> _strip = null!;

        [GlobalSetup]
        public void Setup()
        {
            var rows = FirewallRows.Rows(1000);
            const string sep = "~^~";
            var sb = new StringBuilder();
            foreach (var r in rows)
            {
                sb.Append(string.Join(sep, r.Keys)).Append("\r\n");
                sb.Append(string.Join(sep, r.Values)).Append("\r\n");
            }
            string dsv = sb.ToString();

            // What a terminal sends for that answer: the DSV text plus the colour and cursor sequences RouterOS wraps
            // lines in (one SGR pair and one erase-line per line).
            var raw = new StringBuilder();
            foreach (var line in dsv.Split('\n'))
                raw.Append("\x1b[m\x1b[36m").Append(line.TrimEnd('\r')).Append("\x1b[m\x1b[K\r\n");
            _raw = raw.ToString();

            _strip = (Func<string, string>)Internal.Method("tik4net.Cli.VtStripper", "StripAnsi", typeof(string)).CreateDelegate(typeof(Func<string, string>));
        }

        /// <summary>
        /// <c>CliConsole.ReadCommandResponseAsync</c>'s loop: every poll that received bytes takes <c>StringBuilder.ToString()</c>
        /// of all that arrived and strips all of it again (a poll that received nothing keeps the last strip). Here the
        /// answer arrives in 64 equal slices, i.e. 64 polls that each saw new bytes.
        /// </summary>
        [Benchmark(Description = "per-poll ToString + StripAnsi of the buffer — 64 polls")]
        public int StripPerPoll()
        {
            var acc = new StringBuilder();
            int slice = _raw.Length / 64 + 1, total = 0;
            for (int at = 0; at < _raw.Length; at += slice)
            {
                acc.Append(_raw, at, Math.Min(slice, _raw.Length - at));
                total += _strip(acc.ToString()).Length;
            }
            return total;
        }
    }
}
