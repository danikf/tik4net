// Nullable-enabled on its own: the test project as a whole is not (see the note in
// Directory.Build.props), but this file implements ITikWireTraceSink, whose signature is annotated.
#nullable enable

using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using tik4net.Cli;
using tik4net.Diagnostics;

namespace tik4net.unittests.Connection
{
    /// <summary>
    /// A wire trace must never carry the password.
    /// </summary>
    /// <remarks>
    /// <see cref="TikWireTrace"/> is a public diagnostic surface, it is what the MCP tool exposes as
    /// <c>traceLevel</c>, and the documentation tells people to turn it on when a transport misbehaves — so
    /// its output is, by design, the thing a user copies into a bug report. Anything in it is published.
    /// <para>
    /// The credential reaches the wire two different ways and needs two different defences: the binary API
    /// sends it as a <b>named word</b> the tracer can recognise on sight, while a terminal transport just
    /// <b>types</b> it, and raw bytes on a socket carry no clue about what they mean — there only the code
    /// doing the typing knows, so it marks the moment with <see cref="TikWireTrace.Secret"/>.
    /// </para>
    /// </remarks>
    [TestClass]
    public class WireTraceRedactionTests
    {
        private const string Password = "hunter2-NotInTheTrace";

        /// <summary>Records everything a sink is handed, rendered exactly as a reader would see it.</summary>
        private sealed class CapturingSink : ITikWireTraceSink
        {
            public readonly List<string> Lines = new List<string>();

            public void Emit(string channel, TikWireDir dir, byte[]? data, int offset, int count, string? note)
                => Lines.Add(channel + " " + dir + " [" + TikWireTrace.Escape(data, offset, count) + "] " + note);

            public string All => string.Join(Environment.NewLine, Lines);
        }

        [TestMethod]
        public void ThePasswordWordOfAnApiLoginIsNotRendered()
        {
            var sink = new CapturingSink();
            byte[] bytes = Encoding.ASCII.GetBytes("=password=" + Password);

            using (TikWireTrace.Capture(sink))
            {
                TikWireTrace.EmitWord("api.word", TikWireDir.Send, bytes, 0, bytes.Length,
                    "=password=" + Password);
            }

            StringAssert.Contains(sink.All, "=password=<redacted>",
                "the word's name stays — knowing a password word went out is the diagnostic value");
            Assert.IsFalse(sink.All.Contains(Password), "the password reached the trace: " + sink.All);
        }

        /// <summary>
        /// The challenge response of the pre-6.43 login protocol is redacted too: it is replayable against
        /// the same challenge, so publishing it is publishing a credential.
        /// </summary>
        [TestMethod]
        public void TheChallengeResponseIsTreatedAsACredential()
        {
            Assert.IsTrue(TikWireTrace.IsSecretWord("=response=00abcdef"));
            Assert.IsTrue(TikWireTrace.IsSecretWord("=password=x"));
            Assert.IsFalse(TikWireTrace.IsSecretWord("=name=admin"),
                "the user name is not a secret, and blanking it would cost a real diagnostic");
            Assert.IsFalse(TikWireTrace.IsSecretWord("/login"));
        }

        /// <summary>
        /// The one that matters: the real CLI login routine, driven with fake I/O, with a sink watching.
        /// </summary>
        /// <remarks>
        /// Not a test of <see cref="TikWireTrace.Secret"/> in isolation — that would pass whether or not
        /// <see cref="RouterOsCliLogin"/> ever called it. The delegates here emit to the tracer exactly as
        /// <c>TelnetClient</c> does for every byte it writes, so what this asserts is what a Telnet, SSH,
        /// MAC-Telnet or WinBox-CLI session would actually produce.
        /// </remarks>
        [TestMethod]
        public async Task TheCliLoginDoesNotTypeThePasswordIntoTheTrace()
        {
            var sink = new CapturingSink();

            // The router's side of the dialogue, in order.
            var script = new Queue<string>(new[] { "Login: ", "Password: ", "[admin@rb] > " });
            Task<string> ReadUntil(Func<string, bool> _, CancellationToken __)
                => Task.FromResult(script.Count > 0 ? script.Dequeue() : "[admin@rb] > ");

            // What a terminal transport does with a line: put its bytes on the socket, and trace them.
            Task SendLine(string text, CancellationToken _)
            {
                byte[] b = Encoding.ASCII.GetBytes(text + "\r\n");
                TikWireTrace.Emit("telnet.sock", TikWireDir.Send, b, 0, b.Length);
                return Task.CompletedTask;
            }

            Task SendBytes(byte[] b, CancellationToken _)
            {
                TikWireTrace.Emit("telnet.sock", TikWireDir.Send, b, 0, b.Length);
                return Task.CompletedTask;
            }

            using (TikWireTrace.Capture(sink))
            {
                await RouterOsCliLogin.LoginAsync(
                    "admin", Password, useTerminalFlags: false,
                    readUntil: ReadUntil, sendLine: SendLine, sendBytes: SendBytes,
                    ct: CancellationToken.None);
            }

            Assert.IsFalse(sink.All.Contains(Password),
                "the CLI login typed the password into the wire trace:" + Environment.NewLine + sink.All);
            StringAssert.Contains(sink.All, "redacted",
                "the trace should still show that something was sent and how long it was");
            StringAssert.Contains(sink.All, "admin",
                "only the secret is removed — the user name and the shape of the handshake must survive, "
                + "or the trace stops being useful for the login problems it exists to diagnose");
        }

        /// <summary>The scope has to survive the awaits of a handshake, and has to end when it ends.</summary>
        [TestMethod]
        public async Task TheSecretScopeSurvivesAnAwaitAndIsRestored()
        {
            var sink = new CapturingSink();
            byte[] payload = Encoding.ASCII.GetBytes("VISIBLE");

            using (TikWireTrace.Capture(sink))
            {
                using (TikWireTrace.Secret())
                {
                    await Task.Yield();
                    TikWireTrace.Emit("telnet.sock", TikWireDir.Send, payload, 0, payload.Length);
                }

                TikWireTrace.Emit("telnet.sock", TikWireDir.Send, payload, 0, payload.Length);
            }

            Assert.AreEqual(2, sink.Lines.Count);
            Assert.IsFalse(sink.Lines[0].Contains("VISIBLE"), "the scope did not survive the await");
            Assert.IsTrue(sink.Lines[1].Contains("VISIBLE"), "the scope did not end with its using block");
        }
    }
}
