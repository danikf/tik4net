// Nullable-enabled on its own: the test project as a whole is not (see the note in Directory.Build.props).
#nullable enable
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
    /// Secrets over the CLI transports. RouterOS 7 leaves a sensitive value out of a terminal <c>print</c> unless it
    /// is given <c>show-sensitive</c> (7.24.4: <c>/radius print</c> without it has no <c>secret</c>, with it the
    /// value), while the binary API and REST return it — so every CLI transport read <c>null</c> where the API read
    /// the secret (found by the V2 canonical-form sweep). RouterOS 6 has no such word — 6.49.13 answers
    /// <c>expected end of command</c> — and prints secrets without it.
    /// </summary>
    [TestClass]
    public class CliShowSensitiveTests
    {
        [TikEntity("/box")]
        private sealed class SecretBox
        {
            [TikProperty(".id", IsReadOnly = true, IsMandatory = true)]
            public string? Id { get; set; }

            [TikProperty("name")]
            public string? Name { get; set; }

            [TikProperty("secret", IsSensitive = true)]
            public string? Secret { get; set; }
        }

        [TikEntity("/box")]
        private sealed class PlainBox
        {
            [TikProperty(".id", IsReadOnly = true, IsMandatory = true)]
            public string? Id { get; set; }

            [TikProperty("name")]
            public string? Name { get; set; }
        }

        [TestMethod]
        public void RouterOs7_ASensitiveEntityIsReadWithShowSensitive_AndGetsTheSecret()
        {
            using (var conn = new SecretRouter(knowsShowSensitive: true))
            {
                conn.OpenScripted();

                var rows = conn.LoadAll<SecretBox>().ToList();

                CollectionAssert.AreEqual(new[] { "s0", "s1" }, rows.Select(r => r.Secret).ToList());
                Assert.IsTrue(conn.Reads.All(s => s.Contains(" show-sensitive")), string.Join(" | ", conn.Reads));
            }
        }

        [TestMethod]
        public void AnEntityWithoutSensitiveFields_IsReadWithoutTheWord()
        {
            using (var conn = new SecretRouter(knowsShowSensitive: true))
            {
                conn.OpenScripted();

                conn.LoadAll<PlainBox>().ToList();

                Assert.IsFalse(conn.Sent.Any(s => s.Contains("show-sensitive")), string.Join(" | ", conn.Sent));
            }
        }

        [TestMethod]
        public void RouterOs6_ARefusedWordIsDropped_AndTheSecretStillArrives()
        {
            using (var conn = new SecretRouter(knowsShowSensitive: false))
            {
                conn.OpenScripted();

                var rows = conn.LoadAll<SecretBox>().ToList();

                CollectionAssert.AreEqual(new[] { "s0", "s1" }, rows.Select(r => r.Secret).ToList());
            }
        }

        [TestMethod]
        public void RouterOs6_TheRefusalIsRemembered_SoTheNextReadDoesNotAskAgain()
        {
            using (var conn = new SecretRouter(knowsShowSensitive: false))
            {
                conn.OpenScripted();
                conn.LoadAll<SecretBox>().ToList();
                int first = conn.Sent.Count;

                conn.LoadAll<SecretBox>().ToList();

                Assert.IsFalse(conn.Sent.Skip(first).Any(s => s.Contains("show-sensitive")),
                    string.Join(" | ", conn.Sent.Skip(first)));
            }
        }

        [TestMethod]
        public void ASyntaxErrorTheWordDidNotCause_StillReachesTheCaller()
        {
            using (var conn = new SecretRouter(knowsShowSensitive: true) { AlwaysSyntaxError = true })
            {
                conn.OpenScripted();

                try
                {
                    conn.LoadAll<SecretBox>().ToList();
                    Assert.Fail("the router's syntax error was swallowed");
                }
                catch (TikCommandTrapException ex)
                {
                    StringAssert.Contains(ex.Message, "expected end of command");
                }
            }
        }


        // ── The router double ────────────────────────────────────────────────

        /// <summary>
        /// A menu <c>/box</c> with two rows, each with a secret. <paramref name="knowsShowSensitive"/> chooses the
        /// version: RouterOS 7 hides the secret unless asked with <c>show-sensitive</c>; RouterOS 6 refuses the
        /// word and prints the secret anyway.
        /// </summary>
        private sealed class SecretRouter : CliConnectionBase
        {
            private const string SyntaxError = "expected end of command (line 1 column 40)";
            private readonly bool _knowsShowSensitive;
            public readonly List<string> Sent = new List<string>();
            public IEnumerable<string> Reads => Sent.Where(s => s.Contains("/box print"));
            public bool AlwaysSyntaxError;

            public SecretRouter(bool knowsShowSensitive)
            {
                _knowsShowSensitive = knowsShowSensitive;
                CliFieldSeparator = null;   // scripts the as-value read; the DSV read is CliDsvReadTests
            }

            protected override string TransportName => "Secrets";

            public void OpenScripted()
                => OpenWith(_ => Task.FromResult(0), SendAsync, (raw, ct) => Task.FromResult(string.Empty), () => { });

            private Task<string> SendAsync(string cliText, CancellationToken ct)
            {
                Sent.Add(cliText);
                if (!cliText.Contains("/box print"))
                    return Task.FromResult(SyntaxError);
                if (AlwaysSyntaxError || (!_knowsShowSensitive && cliText.Contains("show-sensitive")))
                    return Task.FromResult(SyntaxError);

                bool showSecret = !_knowsShowSensitive || cliText.Contains("show-sensitive");
                var rows = Enumerable.Range(0, 2)
                    .Select(i => ".id=*" + (i + 1) + ";name=b" + i + (showSecret ? ";secret=s" + i : ""))
                    .ToList();

                var pick = Regex.Match(cliText, @":pick \[[^\[\]]*? find(?: \([^)]*\))?\] (?<from>\d+) (?<to>\d+)\]");
                if (pick.Success)
                {
                    int from = int.Parse(pick.Groups["from"].Value), to = int.Parse(pick.Groups["to"].Value);
                    var window = rows.Skip(from).Take(to - from).ToList();
                    return Task.FromResult(string.Join(";", window) + (char)10 + "#w=" + window.Count);
                }
                return Task.FromResult(CountedReadFake.Answer(cliText, string.Join(";", rows)));
            }

            public override void Open(string host, string user, string password) => OpenScripted();
            public override void Open(string host, int port, string user, string password) => OpenScripted();
            public override Task OpenAsync(string host, string user, string password, CancellationToken cancellationToken = default) { OpenScripted(); return Task.FromResult(0); }
            public override Task OpenAsync(string host, int port, string user, string password, CancellationToken cancellationToken = default) { OpenScripted(); return Task.FromResult(0); }
        }
    }
}
