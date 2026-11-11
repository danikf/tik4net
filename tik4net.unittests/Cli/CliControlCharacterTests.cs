// CliControlCharacterTests.cs — router-free tests: a value cannot change the command line it is typed into.
//
// A CLI transport TYPES its command into RouterOS's line editor, and the editor acts on keys, quoted string or not.
// Measured over Telnet on 7.25.1 and 6.49.21 (2026-11-11):
//
//   where name="x<03>:put T4NINJ"   -> "expected end of command (line 1 column 12)": Ctrl-C dropped the line typed so
//                                      far and ':put T4NINJ"]...' was parsed as a new command
//   where name="x<C2 9B>:put ..."   -> echoed as 'xut T4NINJ': the byte 0x9B is a CSI, and it ate ':p'
//   where name="xě:put ..."         -> the same: UTF-8 'ě' is C4 9B. Any character whose UTF-8 form holds a byte
//                                      0x80-0x9F is a C1 control to the editor
//   "a<TAB>b"                       -> stored as 'a  b' (two spaces)
//
// and what the router stores for the hex escape inside quotes, read back over the API on both versions:
//
//   source="a\03b\1Bc\7Fd\01e\1Ff\00h"  -> exactly those bytes (lowercase '\1b' does not parse: uppercase only)
//   source="x\C4\9By"                   -> x, C4, 9B, y: 'ěy' in UTF-8
//   "\09"                               -> a TAB on 6.49.21, four spaces on 7.25.1 (as '\t' is)
//
// So inside quotes every byte outside printable ASCII is written as \XX: the control characters by the builder (they
// are the same byte in every encoding a terminal uses), the rest by the console, which knows the connection's encoding.

using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using tik4net.Cli;
using tik4net.Telnet;

namespace tik4net.unittests.Cli
{
    [TestClass]
    public class CliControlCharacterTests
    {
        private const string Prompt = "[admin@CHR] > ";

        // ── The builder: control characters ───────────────────────────────────

        /// <summary>The value from the security review: Ctrl-C and a command after it.</summary>
        [TestMethod]
        public void ACtrlC_InAValue_IsWrittenAsItsHexEscape()
        {
            Assert.AreEqual("\"x\\03:put T4NINJ\"", CliCommandBuilder.QuoteIfNeeded("x\u0003:put T4NINJ"));
            Assert.AreEqual("\"x\\03:put T4NINJ\"", CliCommandBuilder.QuoteForWhere("x\u0003:put T4NINJ"));
        }

        /// <summary>
        /// No control character reaches the line editor from a value: every C0 control and DEL comes out as a hex escape,
        /// two uppercase digits. TAB is written <c>\09</c> as well: typed, the editor stores it as two spaces.
        /// </summary>
        [TestMethod]
        public void NoControlCharacter_LeavesTheBuilderRaw()
        {
            var controls = Enumerable.Range(0, 0x20).Select(i => (char)i).Concat(new[] { (char)0x7F });
            foreach (char c in controls)
            {
                string value = "a" + c + "b";
                foreach (string quoted in new[] { CliCommandBuilder.QuoteIfNeeded(value)!, CliCommandBuilder.QuoteForWhere(value) })
                {
                    Assert.IsFalse(quoted.Any(ch => ch < 0x20 || ch == 0x7F), "0x" + ((int)c).ToString("X2") + " left raw in " + quoted);
                    StringAssert.StartsWith(quoted, "\"a\\", "0x" + ((int)c).ToString("X2"));
                }
            }
            Assert.AreEqual("\"a\\1Bb\"", CliCommandBuilder.QuoteIfNeeded("a\u001Bb"));
            Assert.AreEqual("\"a\\7Fb\"", CliCommandBuilder.QuoteIfNeeded("a\u007Fb"));
            Assert.AreEqual("\"a\\09b\"", CliCommandBuilder.QuoteIfNeeded("a\tb"));
            Assert.AreEqual("\"a\\00b\"", CliCommandBuilder.QuoteIfNeeded("a\0b"));
        }

        /// <summary>CR and LF keep the escapes measured before (<see cref="CliQuotingTests"/>).</summary>
        [TestMethod]
        public void CrAndLf_KeepTheirNamedEscapes()
        {
            Assert.AreEqual("\"a\\r\\nb\"", CliCommandBuilder.QuoteIfNeeded("a\r\nb"));
        }

        /// <summary>A backslash is doubled before the hex escapes are added, so a value that spells one is kept.</summary>
        [TestMethod]
        public void AValueSpellingAHexEscape_IsKeptLiterally()
        {
            Assert.AreEqual("\"a\\\\03\\03b\"", CliCommandBuilder.QuoteIfNeeded("a\\03\u0003b"));
        }

        // ── The console: everything else outside printable ASCII ──────────────

        private sealed class RecordingStream : ICliByteStream
        {
            private readonly Queue<byte[]> _chunks = new Queue<byte[]>();
            private bool _released;

            internal RecordingStream(Encoding encoding, string answer) { _chunks.Enqueue(encoding.GetBytes(answer)); }

            internal List<byte[]> Writes { get; } = new List<byte[]>();

            public bool HasData => _released && _chunks.Count > 0;
            public bool ClosedAndDrained => false;

            public Task<int> ReadAsync(byte[] buffer, CancellationToken ct)
            {
                byte[] chunk = _chunks.Dequeue();
                chunk.CopyTo(buffer, 0);
                return Task.FromResult(chunk.Length);
            }

            public Task WriteAsync(byte[] bytes, CancellationToken ct)
            {
                Writes.Add(bytes);
                _released = true;
                return Task.CompletedTask;
            }
        }

        private static byte[] Typed(string command, Encoding encoding, string echo)
        {
            var stream = new RecordingStream(encoding, echo + "\r\n" + Prompt);
            var session = new CliPtySession(stream, encoding, () => 5000, "SSH", "ssh.pty", "\r", _ => { });
            session.Terminal.SendCommandAndReadAsync(command, null, CancellationToken.None).GetAwaiter().GetResult();
            return stream.Writes[0];
        }

        /// <summary>
        /// A character whose UTF-8 form holds a C1 byte (<c>ě</c> = C4 9B, U+009B itself = C2 9B) is written as the hex
        /// escapes of its bytes, so the line editor only ever sees printable ASCII inside a quoted value.
        /// </summary>
        [TestMethod]
        public void NonAscii_InsideQuotes_IsTypedAsTheHexEscapesOfItsBytes()
        {
            const string expected = "/system identity set name=\"x\\C4\\9B\\C2\\9B:put T4NINJ\"\r";
            byte[] written = Typed("/system identity set name=\"x\u011b\u009b:put T4NINJ\"", Encoding.UTF8,
                expected.TrimEnd('\r'));

            Assert.IsFalse(written.Any(b => b >= 0x80), "a byte above 0x7F was typed: " + Encoding.ASCII.GetString(written));
            Assert.AreEqual(expected, Encoding.ASCII.GetString(written));
        }

        /// <summary>The escape is the connection's encoding: <c>ě</c> is one byte, EC, in windows-1250.</summary>
        [TestMethod]
        public void NonAscii_IsEscapedInTheConnectionsEncoding()
        {
            Encoding cp1250 = CodePages.Get(1250);
            byte[] written = Typed(":put \"\u011b\"", cp1250, ":put \"\\EC\"");
            Assert.AreEqual(":put \"\\EC\"\r", Encoding.ASCII.GetString(written));
        }

        /// <summary>
        /// Only the inside of a quoted string is rewritten: an escaped quote does not end it, and what stands outside
        /// the quotes is typed as written (it is the caller's command syntax, not a value).
        /// </summary>
        [TestMethod]
        public void OnlyTheInsideOfAQuotedString_IsRewritten()
        {
            byte[] written = Typed(":put (\"a\\\"\u0003\" . \"\u00e9\")", Encoding.UTF8, ":put (\"a\\\"\\03\" . \"\\C3\\A9\")");
            Assert.AreEqual(":put (\"a\\\"\\03\" . \"\\C3\\A9\")\r", Encoding.ASCII.GetString(written));
        }

        // ── Telnet: IAC ───────────────────────────────────────────────────────

        /// <summary>
        /// A data byte 0xFF is written twice on Telnet, or the router's Telnet server reads it as the start of a command.
        /// It needs a single-byte encoding to occur at all: Latin-1 <c>ÿ</c>, outside quotes and in a login line.
        /// </summary>
        [TestMethod]
        [DoNotParallelize]
        public void Telnet_DoublesADataByte0xFF()
        {
            Encoding latin1 = CodePages.Get(28591);
            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            try
            {
                var client = new TelnetClient(latin1, CliClientTimeouts.Fixed(5000, connectMs: 2000, sendMs: 5000));
                client.Connect(IPAddress.Loopback.ToString(), ((IPEndPoint)listener.LocalEndpoint).Port);
                using (Socket router = listener.AcceptSocket())
                {
                    var sending = Task.Run(() => client.Terminal.SendCommandAndReadAsync(":put \u00ff", null, CancellationToken.None));

                    var received = new List<byte>();
                    var buffer = new byte[256];
                    while (!received.Contains((byte)'\n') && router.Poll(3_000_000, SelectMode.SelectRead))
                    {
                        int n = router.Receive(buffer);
                        if (n == 0) break;
                        received.AddRange(buffer.Take(n));
                    }
                    router.Send(Encoding.ASCII.GetBytes(":put \r\n" + Prompt));

                    CollectionAssert.AreEqual(Encoding.ASCII.GetBytes(":put ").Concat(new byte[] { 0xFF, 0xFF, 0x0D, 0x0A }).ToArray(),
                        received.ToArray());
                    sending.Wait(5000);
                    client.Close();
                }
            }
            finally { listener.Stop(); }
        }

        private static class CodePages
        {
            internal static Encoding Get(int codePage)
            {
#if NETCOREAPP
                Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
#endif
                return Encoding.GetEncoding(codePage);
            }
        }
    }
}
