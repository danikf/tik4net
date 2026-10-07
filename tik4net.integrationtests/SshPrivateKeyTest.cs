using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using tik4net.Ssh;

namespace tik4net.integrationtests
{
    /// <summary>
    /// SSH login with a private key (<see cref="TikConnectionSetup.SshPrivateKey"/>): a throwaway user with a password
    /// nobody knows and a freshly generated key registered under <c>/user/ssh-keys</c>.
    /// </summary>
    /// <remarks>
    /// The key is RSA because .NET Framework can generate it; RouterOS takes <c>ssh-rsa</c> and <c>ssh-ed25519</c>. The
    /// public key goes in with <c>/user/ssh-keys add key=</c> on RouterOS 7, and as a file <c>/user/ssh-keys import</c>
    /// reads on 6, which has no <c>add</c>.
    /// </remarks>
    [TestClass]
    [TestCategory(TestCategories.LegIndependent)]
    [TestCategory(TestCategories.AnyRouter)]
    [SafeInParallelLegs]
    public class SshPrivateKeyTest : LockedTestBase
    {
        [ClassInitialize]
        public static void RegisterSsh(TestContext context) => Tik4NetSsh.Register();

        [TestMethod]
        public void AKeyRegisteredForTheUser_LogsIn_AndAnotherKeyIsRefused()
        {
            string host = LabConfig.Get("host");
            string user = "t4n-sshkey-" + Guid.NewGuid().ToString("N").Substring(0, 8);
            var key = new TestRsaKey();
            var stranger = new TestRsaKey();

            using (var admin = ConnectionFactory.CreateConnection(TikConnectionType.Api))
            {
                admin.Open(host, LabConfig.Get("user"), LabConfig.Get("pass"));
                admin.CreateCommandAndParameters("/user/add", "name", user, "group", "full",
                    "password", Guid.NewGuid().ToString("N")).ExecuteNonQuery();
                try
                {
                    RegisterPublicKey(admin, user, key.PublicKeyLine("tik4net-test"));

                    using (var conn = new TikConnectionSetup(host, user, "") { SshPrivateKey = key.PrivateKeyPem }
                               .CreateUnopened(TikConnectionType.Ssh))
                    {
                        conn.Open(host, user, "");
                        string identity = conn.CreateCommand("/system/identity/print").ExecuteScalar("name");
                        Assert.IsFalse(string.IsNullOrEmpty(identity), "logged in with the key, but the identity read was empty");
                    }

                    using (var conn = new TikConnectionSetup(host, user, "") { SshPrivateKey = stranger.PrivateKeyPem }
                               .CreateUnopened(TikConnectionType.Ssh))
                    {
                        Assert.ThrowsException<TikConnectionLoginException>(() => conn.Open(host, user, ""),
                            "a key the router does not know for this user must be refused");
                    }
                }
                finally
                {
                    foreach (var row in admin.CreateCommandAndParameters("/user/ssh-keys/print",
                                 TikCommandParameterFormat.Filter, "user", user).ExecuteList())
                        admin.CreateCommandAndParameters("/user/ssh-keys/remove", ".id", row.GetId()).ExecuteNonQuery();
                    foreach (var row in admin.CreateCommandAndParameters("/user/print",
                                 TikCommandParameterFormat.Filter, "name", user).ExecuteList())
                        admin.CreateCommandAndParameters("/user/remove", ".id", row.GetId()).ExecuteNonQuery();
                }
            }
        }

        // RouterOS 7 takes the key line itself; 6 has no 'add' and imports a file, which '/file print file=' creates
        // (as <name>.txt) and 'set contents=' fills.
        private static void RegisterPublicKey(ITikConnection admin, string user, string publicKeyLine)
        {
            string version = admin.CreateCommand("/system/resource/print").ExecuteScalar("version");
            if (!version.StartsWith("6.", StringComparison.Ordinal))
            {
                admin.CreateCommandAndParameters("/user/ssh-keys/add", "user", user, "key", publicKeyLine).ExecuteNonQuery();
                return;
            }

            string file = user + ".txt";
            admin.CreateCommandAndParameters("/file/print", TikCommandParameterFormat.NameValue, "file", user).ExecuteList();
            try
            {
                // The file appears a moment after the print that creates it.
                string id = null;
                for (int i = 0; i < 30 && id == null; i++)
                {
                    id = admin.CreateCommandAndParameters("/file/print", TikCommandParameterFormat.Filter, "name", file)
                        .ExecuteList().SingleOrDefault()?.GetId();
                    if (id == null) System.Threading.Thread.Sleep(100);
                }
                Assert.IsNotNull(id, "'/file print file=" + user + "' created no " + file);
                admin.CreateCommandAndParameters("/file/set", ".id", id, "contents", publicKeyLine).ExecuteNonQuery();
                admin.CreateCommandAndParameters("/user/ssh-keys/import", "user", user, "public-key-file", file).ExecuteNonQuery();
            }
            finally
            {
                foreach (var row in admin.CreateCommandAndParameters("/file/print", TikCommandParameterFormat.Filter, "name", file)
                             .ExecuteList())
                    admin.CreateCommandAndParameters("/file/remove", ".id", row.GetId()).ExecuteNonQuery();
            }
        }

        /// <summary>An RSA key pair as the two texts SSH needs: the PEM private key and the OpenSSH public key line.</summary>
        private sealed class TestRsaKey
        {
            private readonly RSAParameters _p;

            internal TestRsaKey()
            {
                using (var rsa = new RSACryptoServiceProvider(2048))
                    _p = rsa.ExportParameters(true);
            }

            // PKCS#1 RSAPrivateKey: SEQUENCE { version 0, n, e, d, p, q, dp, dq, qinv }.
            internal string PrivateKeyPem
            {
                get
                {
                    byte[] der = Sequence(Integer(new byte[] { 0 }), Integer(_p.Modulus), Integer(_p.Exponent), Integer(_p.D),
                        Integer(_p.P), Integer(_p.Q), Integer(_p.DP), Integer(_p.DQ), Integer(_p.InverseQ));
                    string b64 = Convert.ToBase64String(der, Base64FormattingOptions.InsertLineBreaks);
                    return "-----BEGIN RSA PRIVATE KEY-----\n" + b64.Replace("\r\n", "\n") + "\n-----END RSA PRIVATE KEY-----\n";
                }
            }

            // RFC 4253: string "ssh-rsa", mpint e, mpint n.
            internal string PublicKeyLine(string comment)
            {
                using (var ms = new MemoryStream())
                {
                    WriteString(ms, Encoding.ASCII.GetBytes("ssh-rsa"));
                    WriteString(ms, Unsigned(_p.Exponent));
                    WriteString(ms, Unsigned(_p.Modulus));
                    return "ssh-rsa " + Convert.ToBase64String(ms.ToArray()) + " " + comment;
                }
            }

            private static void WriteString(Stream s, byte[] data)
            {
                s.Write(new[] { (byte)(data.Length >> 24), (byte)(data.Length >> 16), (byte)(data.Length >> 8), (byte)data.Length }, 0, 4);
                s.Write(data, 0, data.Length);
            }

            // A positive big-endian integer: no leading zeros, but one zero byte when the high bit is set.
            private static byte[] Unsigned(byte[] value)
            {
                byte[] trimmed = value.SkipWhile((b, i) => b == 0 && i < value.Length - 1).ToArray();
                return trimmed[0] >= 0x80 ? new byte[] { 0 }.Concat(trimmed).ToArray() : trimmed;
            }

            private static byte[] Integer(byte[] value) => Tlv(0x02, Unsigned(value));

            private static byte[] Sequence(params byte[][] items) => Tlv(0x30, items.SelectMany(i => i).ToArray());

            private static byte[] Tlv(byte tag, byte[] content)
            {
                byte[] length = content.Length < 0x80
                    ? new[] { (byte)content.Length }
                    : content.Length < 0x100
                        ? new byte[] { 0x81, (byte)content.Length }
                        : new byte[] { 0x82, (byte)(content.Length >> 8), (byte)content.Length };
                return new[] { tag }.Concat(length).Concat(content).ToArray();
            }
        }
    }
}
