using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using tik4net.Cli;
using tik4net.Objects;

namespace tik4net.unittests.Cli
{
    /// <summary>
    /// Whether a router understands <c>:serialize</c> is learnt from what it answers — and an empty window is no
    /// answer. A window runs its print only when it holds ids (<c>:if ([:len $w] &gt; 0) do={ … }</c>), so on
    /// RouterOS 6.49.13 a free-text menu with no rows came back clean, the connection concluded "serialize works",
    /// and the next free-text menu WITH rows failed with <c>bad command name serialize</c> and no fallback.
    /// </summary>
    [TestClass]
    public class CliSerializeDiscoveryTests
    {
        [TikEntity("/empty")]
        private sealed class EmptyFreeText
        {
            [TikProperty(".id", IsReadOnly = true, IsMandatory = true)] public string? Id { get; set; }
            [TikProperty("comment", IsFreeText = true)] public string? Comment { get; set; }
        }

        [TikEntity("/full")]
        private sealed class FullFreeText
        {
            [TikProperty(".id", IsReadOnly = true, IsMandatory = true)] public string? Id { get; set; }
            [TikProperty("comment", IsFreeText = true)] public string? Comment { get; set; }
        }

        [TestMethod]
        public void AnEmptyWindowDoesNotProveSerializeWorks()
        {
            using (var conn = new Pre713Connection(new Dictionary<string, int> { ["/empty"] = 0, ["/full"] = 2 }))
            {
                conn.OpenScripted();
                conn.CliReadPageSize = 5;

                Assert.AreEqual(0, conn.LoadList<EmptyFreeText>().Count());
                var rows = conn.LoadList<FullFreeText>().ToList();

                CollectionAssert.AreEqual(new[] { "c0", "c1" }, rows.Select(r => r.Comment).ToList(),
                    "the second read must fall back to as-value: " + string.Join(" | ", conn.Sent));
            }
        }

        /// <summary>
        /// A router before 7.13: <c>:serialize</c> is a bad command name — wherever it is actually run. A window
        /// with no ids never runs its print, so it answers just its size.
        /// </summary>
        private sealed class Pre713Connection : CliConnectionBase
        {
            private const string Refusal = "bad command name serialize (line 1 column 63)";
            private readonly Dictionary<string, int> _rows;
            public readonly List<string> Sent = new List<string>();

            public Pre713Connection(Dictionary<string, int> rows) => _rows = rows;

            protected override string TransportName => "Pre713";

            public void OpenScripted()
                => OpenWith(_ => Task.FromResult(0), SendAsync, (raw, ct) => Task.FromResult(string.Empty), () => { });

            private Task<string> SendAsync(string cliText, CancellationToken ct)
            {
                Sent.Add(cliText);
                bool json = cliText.Contains(":serialize");
                var menu = Regex.Match(cliText, @"\[(/\w+) ");
                int count = menu.Success && _rows.TryGetValue(menu.Groups[1].Value, out int n) ? n : 0;

                var pick = Regex.Match(cliText, @":pick \[[^\[\]]*? find[^\]]*\] (?<from>\d+) (?<to>\d+)\]");
                if (!pick.Success)
                    return Task.FromResult(json ? Refusal : Rows(count) + (char)10 + "#n=" + count + "/num");

                int from = int.Parse(pick.Groups["from"].Value), to = int.Parse(pick.Groups["to"].Value);
                int window = Math.Max(0, Math.Min(count, to) - from);
                if (window == 0)
                    return Task.FromResult("#w=0");
                return Task.FromResult(json ? Refusal : Rows(window) + (char)10 + "#w=" + window);
            }

            private static string Rows(int n)
                => string.Join(";", Enumerable.Range(0, n).Select(r => ".id=*" + r + ";comment=c" + r));

            public override void Open(string host, string user, string password) => OpenScripted();
            public override void Open(string host, int port, string user, string password) => OpenScripted();
            public override Task OpenAsync(string host, string user, string password, CancellationToken cancellationToken = default) { OpenScripted(); return Task.FromResult(0); }
            public override Task OpenAsync(string host, int port, string user, string password, CancellationToken cancellationToken = default) { OpenScripted(); return Task.FromResult(0); }
        }
    }
}
