#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using tik4net;

namespace tik4net.unittests.Connection
{
    /// <summary>
    /// <see cref="TikPassword"/>: the password prefixes, where each is read (every one in a connection string;
    /// <c>plain:</c> and <c>dpapi:</c> only in a setup's constructor and in <c>Open</c>), and that no error carries the
    /// password, the file's text or the blob.
    /// </summary>
    [TestClass]
    public class TikPasswordTests
    {
        private const string Secret = "S3cr3t-ěščř";
        private const string EnvName = "TIK4NET_UNITTEST_PASSWORD";
        private string _dir = null!;

        [TestInitialize]
        public void Init()
        {
            _dir = Path.Combine(Path.GetTempPath(), "tik4net-pw-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_dir);
            Environment.SetEnvironmentVariable(EnvName, Secret);
        }

        [TestCleanup]
        public void Cleanup()
        {
            Environment.SetEnvironmentVariable(EnvName, null);
            try { Directory.Delete(_dir, recursive: true); } catch (IOException) { }
        }

        private string WriteFile(string name, byte[] content)
        {
            string path = Path.Combine(_dir, name);
            File.WriteAllBytes(path, content);
            return path;
        }

        private string WriteFile(string name, string content) => WriteFile(name, new UTF8Encoding(false).GetBytes(content));

        private static void AssertLeaksNothing(Exception ex, params string[] secrets)
        {
            for (var e = ex; e != null; e = e.InnerException)
                foreach (string s in secrets)
                    Assert.IsFalse(e.Message.Contains(s) || e.ToString().Contains(s), "the error carries a secret: " + e.Message);
        }

        // ── the prefixes ─────────────────────────────────────────────────────

        [DataTestMethod]
        [DataRow("secret")]
        [DataRow("")]
        [DataRow("abc:def")]
        [DataRow("dpap:x")]
        [DataRow("environment:X")]
        [DataRow(":x")]
        public void AValueWithNoKnownPrefix_IsThePassword(string value)
            => Assert.AreEqual(value, TikPassword.Resolve(value));

        [DataTestMethod]
        [DataRow("plain:abc", "abc")]
        [DataRow("PLAIN:abc", "abc")]
        [DataRow("plain:", "")]
        [DataRow("plain:env:X", "env:X")]
        [DataRow("plain:plain:x", "plain:x")]
        public void Plain_IsTheRestUnchanged(string value, string expected)
            => Assert.AreEqual(expected, TikPassword.Resolve(value));

        [TestMethod]
        public void Plain_WritesAValueThatReadsBackUnchanged()
        {
            foreach (string pw in new[] { "x", "", "env:X", "dpapi:abc", "plain:y", "file:/etc/passwd" })
            {
                Assert.AreEqual(pw, TikPassword.Resolve(TikPassword.Plain(pw)));
                Assert.AreEqual(pw, new TikConnectionSetup("192.0.2.1", "u", TikPassword.Plain(pw)).Password);
            }
        }

        [TestMethod]
        public void Env_ReadsTheVariable_CaseInsensitivePrefix()
        {
            Assert.AreEqual(Secret, TikPassword.Resolve("env:" + EnvName));
            Assert.AreEqual(Secret, TikPassword.Resolve("ENV:" + EnvName));
        }

        [TestMethod]
        public void Env_AnUnsetVariable_ThrowsNamingIt()
        {
            var ex = Assert.ThrowsException<ArgumentException>(() => TikPassword.Resolve("env:TIK4NET_UNITTEST_NOT_SET"));
            StringAssert.Contains(ex.Message, "TIK4NET_UNITTEST_NOT_SET");
            Assert.ThrowsException<ArgumentException>(() => TikPassword.Resolve("env:"));
        }

        [DataTestMethod]
        [DataRow("one-line")]
        [DataRow("one-line\n")]
        [DataRow("one-line\r\n")]
        [DataRow("﻿one-line\n")]
        public void File_ReadsOneLine_OneTrailingBreakAndABomRemoved(string content)
            => Assert.AreEqual("one-line", TikPassword.Resolve("file:" + WriteFile("pw.txt", content)));

        [TestMethod]
        public void File_KeepsSpacesAndNonAscii()
            => Assert.AreEqual(" a ěš b ", TikPassword.Resolve("FILE:" + WriteFile("pw.txt", " a ěš b \n")));

        [DataTestMethod]
        [DataRow("line1\nline2")]
        [DataRow("line1\n\n")]
        [DataRow("a\rb")]
        [DataRow("a\tb")]
        [DataRow("a\0b")]
        [DataRow("-----BEGIN OPENSSH PRIVATE KEY-----\nb3BlbnNzaC1rZXktdjEAAAAA\n-----END OPENSSH PRIVATE KEY-----\n")]
        public void File_MoreThanOneLineOrAControlCharacter_IsRefused_WithoutItsText(string content)
        {
            string path = WriteFile("pw.txt", content);
            var ex = Assert.ThrowsException<ArgumentException>(() => TikPassword.Resolve("file:" + path));
            StringAssert.Contains(ex.Message, path);
            AssertLeaksNothing(ex, "line2", "b3BlbnNzaC1rZXktdjEAAAAA");
        }

        [TestMethod]
        public void File_LargerThan1KiB_IsRefused()
        {
            Assert.AreEqual(new string('x', 1024), TikPassword.Resolve("file:" + WriteFile("ok.txt", new string('x', 1024))));
            var ex = Assert.ThrowsException<ArgumentException>(
                () => TikPassword.Resolve("file:" + WriteFile("big.txt", new string('x', 1025))));
            StringAssert.Contains(ex.Message, "1024");
        }

        [TestMethod]
        public void File_NotUtf8_IsRefused()
        {
            string path = WriteFile("utf16.txt", Encoding.Unicode.GetBytes("secret"));   // what PowerShell 5's Out-File writes
            Assert.ThrowsException<ArgumentException>(() => TikPassword.Resolve("file:" + path));
        }

        [TestMethod]
        public void File_Missing_ThrowsNamingThePath()
        {
            string path = Path.Combine(_dir, "nope.txt");
            var ex = Assert.ThrowsException<ArgumentException>(() => TikPassword.Resolve("file:" + path));
            StringAssert.Contains(ex.Message, path);
            Assert.ThrowsException<ArgumentException>(() => TikPassword.Resolve("file:"));
        }

        [TestMethod]
        public void Sources_LimitWhichPrefixesAreRead()
        {
            string file = "file:" + WriteFile("pw.txt", "x");
            Assert.ThrowsException<ArgumentException>(() => TikPassword.Resolve("env:" + EnvName, TikPasswordSources.File));
            Assert.ThrowsException<ArgumentException>(() => TikPassword.Resolve(file, TikPasswordSources.Environment));
            Assert.ThrowsException<ArgumentException>(() => TikPassword.Resolve("dpapi:AAAA", TikPasswordSources.None));
            Assert.AreEqual("x", TikPassword.Resolve("plain:x", TikPasswordSources.None));
            Assert.AreEqual("abc:x", TikPassword.Resolve("abc:x", TikPasswordSources.None));
        }

        // ── DPAPI ────────────────────────────────────────────────────────────

        [DataTestMethod]
        [DataRow(false)]
        [DataRow(true)]
        public void Dpapi_RoundTrips_OnWindows(bool machineScope)
        {
            if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                Assert.Inconclusive("DPAPI is Windows-only; DpapiOffWindows_IsPlatformNotSupported covers the other legs.");
            string value = TikPassword.ProtectWithDpapi(Secret, machineScope);
            StringAssert.StartsWith(value, "dpapi:");
            Assert.IsFalse(value.Contains(Secret));
            Assert.AreEqual(Secret, TikPassword.Resolve(value));
            Assert.AreEqual(Secret, TikPassword.Resolve("DPAPI:" + value.Substring("dpapi:".Length)));
        }

        [TestMethod]
        public void Dpapi_ABlobMadeByProtectedData_Decrypts()
        {
            if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                Assert.Inconclusive("DPAPI is Windows-only.");
            // The wiki's PowerShell line calls ProtectedData.Protect(bytes, null, CurrentUser): the same blob.
#pragma warning disable CA1416 // guarded above
            byte[] blob = System.Security.Cryptography.ProtectedData.Protect(Encoding.UTF8.GetBytes(Secret), null,
                System.Security.Cryptography.DataProtectionScope.CurrentUser);
#pragma warning restore CA1416
            Assert.AreEqual(Secret, TikPassword.Resolve("dpapi:" + Convert.ToBase64String(blob)));
        }

        [TestMethod]
        public void Dpapi_ABadValue_IsAnArgumentException_WithoutTheBlob()
        {
            if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                Assert.Inconclusive("DPAPI is Windows-only.");
            Assert.ThrowsException<ArgumentException>(() => TikPassword.Resolve("dpapi:not base64!"));
            string garbage = Convert.ToBase64String(Enumerable.Range(1, 64).Select(i => (byte)i).ToArray());
            var ex = Assert.ThrowsException<ArgumentException>(() => TikPassword.Resolve("dpapi:" + garbage));
            StringAssert.Contains(ex.Message, "does not decrypt");
            AssertLeaksNothing(ex, garbage);
        }

        [TestMethod]
        public void DpapiOffWindows_IsPlatformNotSupported()
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                Assert.Inconclusive("Runs on the Linux leg of CI.");
            var ex = Assert.ThrowsException<PlatformNotSupportedException>(() => TikPassword.Resolve("dpapi:AAAA"));
            StringAssert.Contains(ex.Message, "env:");
            Assert.ThrowsException<PlatformNotSupportedException>(() => TikPassword.ProtectWithDpapi("x"));
        }

        // ── the connection string reads every prefix ─────────────────────────

        [TestMethod]
        public void ConnectionString_ReadsEnvAndFile_OnBothPasswordKeys()
        {
            string file = WriteFile("agent.txt", "agent-pw\n");
            var setup = TikConnectionSetup.FromConnectionString(
                $"transport=Telnet;host=AA:BB:CC:DD:EE:01;user=t;password=env:{EnvName};romon.host=192.0.2.9;romon.user=a;"
                + $"romon.password=\"file:{file}\"");
            Assert.AreEqual(Secret, setup.Password);
            Assert.AreEqual("agent-pw", setup.RomonAgentSetup!.Password);
            Assert.IsFalse(setup.ToString().Contains(Secret), "ToString still masks");
            StringAssert.Contains(setup.ToString(), "password=***");
        }

        [TestMethod]
        public void ConnectionString_PwdAlias_IsReadToo()
            => Assert.AreEqual(Secret, TikConnectionSetup.FromConnectionString($"host=r;user=u;pwd=env:{EnvName}").Password);

        [TestMethod]
        public void ConnectionString_ReadsAPrefixOnce()
        {
            Assert.AreEqual("plain:abc", TikConnectionSetup.FromConnectionString("host=r;user=u;password=plain:plain:abc").Password);
            Assert.AreEqual("env:X", TikConnectionSetup.FromConnectionString("host=r;user=u;password=plain:env:X").Password);
            Environment.SetEnvironmentVariable(EnvName, "dpapi:not-a-blob");
            Assert.AreEqual("dpapi:not-a-blob", TikConnectionSetup.FromConnectionString($"host=r;user=u;password=env:{EnvName}").Password,
                "a variable's value is the password, not another reference");
        }

        [TestMethod]
        public void ConnectionString_WithSources_RefusesTheOthers_NamingTheKey()
        {
            var ex = Assert.ThrowsException<ArgumentException>(
                () => TikConnectionSetup.FromConnectionString($"host=r;user=u;password=env:{EnvName}", TikPasswordSources.None));
            StringAssert.Contains(ex.Message, "password");
            StringAssert.Contains(ex.Message, "env:");
            ex = Assert.ThrowsException<ArgumentException>(() => TikConnectionSetup.FromConnectionString(
                $"host=AA:BB:CC:DD:EE:01;user=u;romon.host=192.0.2.9;romon.user=a;romon.password=env:{EnvName}", TikPasswordSources.Dpapi));
            StringAssert.Contains(ex.Message, "romon.password");
            Assert.AreEqual("x", TikConnectionSetup.FromConnectionString("host=r;user=u;password=plain:x", TikPasswordSources.None).Password);
        }

        [TestMethod]
        public void ConnectionString_Errors_NameTheKey_NeverTheValue()
        {
            var ex = Assert.ThrowsException<ArgumentException>(
                () => TikConnectionSetup.FromConnectionString("host=r;user=u;password=env:TIK4NET_UNITTEST_NOT_SET"));
            StringAssert.Contains(ex.Message, "password");
            string multi = WriteFile("multi.txt", "first-line-secret\nsecond");
            ex = Assert.ThrowsException<ArgumentException>(
                () => TikConnectionSetup.FromConnectionString($"host=r;user=u;password=\"file:{multi}\""));
            AssertLeaksNothing(ex, "first-line-secret");
        }

        // ── the constructors and Open read plain: and dpapi:, and refuse env: and file: ──

        [DataTestMethod]
        [DataRow("env:" + EnvName)]
        [DataRow("File:/etc/passwd")]
        public void SetupConstructors_RefuseEnvAndFile_WithTheWayOut(string value)
        {
            var ex = Assert.ThrowsException<ArgumentException>(() => new TikConnectionSetup("192.0.2.1", "u", value));
            Assert.AreEqual("password", ex.ParamName);
            StringAssert.Contains(ex.Message, "plain:");
            StringAssert.Contains(ex.Message, "TikPassword.Resolve");
            Assert.ThrowsException<ArgumentException>(() => new TikRomonAgentSetup("192.0.2.1", "u", value));
        }

        [TestMethod]
        public void SetupConstructors_ReadPlain()
        {
            Assert.AreEqual("env:X", new TikConnectionSetup("192.0.2.1", "u", "plain:env:X").Password);
            Assert.AreEqual("x", new TikRomonAgentSetup("192.0.2.1", "u", "PLAIN:x").Password);
        }

        [TestMethod]
        public void SetupConstructors_ReadDpapi_OnWindows()
        {
            if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                Assert.Inconclusive("DPAPI is Windows-only.");
            string value = TikPassword.ProtectWithDpapi(Secret);
            Assert.AreEqual(Secret, new TikConnectionSetup("192.0.2.1", "u", value).Password);
            Assert.AreEqual(Secret, new TikRomonAgentSetup("192.0.2.1", "u", value).Password);
            Assert.AreEqual(Secret, TikConnectionSetup.FromConnectionString("host=r;user=u;password=" + value).Password);
        }

        public static System.Collections.Generic.IEnumerable<object[]> Transports
            => Enum.GetValues(typeof(TikConnectionType)).Cast<TikConnectionType>().Select(t => new object[] { t });

        [DataTestMethod]
        [DynamicData(nameof(Transports))]
        public void EveryTransportsOpen_RefusesEnvAndFile_BeforeItConnects(TikConnectionType type)
        {
            tik4net.Ssh.Tik4NetSsh.Register();
            // 192.0.2.1 is TEST-NET-1: were the password not refused first, the open would wait on a connect.
            using var conn = new TikConnectionSetup("192.0.2.1", "u", "p") { ConnectTimeout = TimeSpan.FromMilliseconds(1) }
                .CreateUnopened(type);
            var ex = Assert.ThrowsException<ArgumentException>(() => conn.Open("192.0.2.1", 1, "u", "env:" + EnvName));
            Assert.AreEqual("password", ex.ParamName);
            AssertLeaksNothing(ex, Secret);
            Assert.ThrowsException<ArgumentException>(() => conn.Open("192.0.2.1", 1, "u", "file:/etc/passwd"));
            var aex = AssertThrowsAsync<ArgumentException>(() => conn.OpenAsync("192.0.2.1", 1, "u", "env:" + EnvName));
            Assert.AreEqual("password", aex.ParamName);
        }

        private static T AssertThrowsAsync<T>(Func<Task> action) where T : Exception
        {
            try { action().GetAwaiter().GetResult(); }
            catch (T ex) { return ex; }
            Assert.Fail("expected " + typeof(T).Name);
            return null!;
        }

        // ── the setup hands Open the password itself ─────────────────────────

        [TestMethod]
        public void TheSetupHandsOpen_ThePasswordItself_EscapedOnlyWhenItLooksLikeAPrefix()
        {
            var plain = new RecordingConnection();
            new TikConnectionSetup("192.0.2.1", "u", "secret").Open(plain);
            Assert.AreEqual("secret", plain.OpenPassword, "a connection that knows nothing of prefixes sees the password");

            var prefixed = new RecordingConnection();
            var setup = new TikConnectionSetup("192.0.2.1", "u", "plain:env:X");
            setup.Open(prefixed);
            Assert.AreEqual("plain:env:X", prefixed.OpenPassword);
            Assert.AreEqual("env:X", TikPassword.Resolve(prefixed.OpenPassword!), "and Open reads it back as the password");
        }

        private sealed class RecordingConnection : tik4net.Connection.TikCommandConnectionBase
        {
            public string? OpenPassword;
            public override TikConnectionCapability Capabilities => TikConnectionCapability.Crud;
            protected override IList<tik4net.Connection.TikRecordSentence> RunPrint(tik4net.Connection.TikCommandDescriptor descriptor) => throw new NotSupportedException();
            protected override string RunAdd(tik4net.Connection.TikCommandDescriptor descriptor) => throw new NotSupportedException();
            protected override void RunNonQuery(tik4net.Connection.TikCommandDescriptor descriptor) => throw new NotSupportedException();
            protected override string RunRawText(tik4net.Connection.TikCommandDescriptor descriptor) => throw new NotSupportedException();
            public override void Close() { }
            public override void Open(string host, int port, string user, string password) => OpenPassword = password;
            public override void Open(string host, string user, string password) => OpenPassword = password;
            public override Task OpenAsync(string host, int port, string user, string password, CancellationToken cancellationToken = default)
            { OpenPassword = password; return Task.CompletedTask; }
            public override Task OpenAsync(string host, string user, string password, CancellationToken cancellationToken = default)
            { OpenPassword = password; return Task.CompletedTask; }
        }
    }
}
