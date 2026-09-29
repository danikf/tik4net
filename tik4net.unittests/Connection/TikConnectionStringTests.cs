using System;
using System.Linq;
using System.Reflection;
using System.Text;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using tik4net;

namespace tik4net.unittests.Connection
{
    /// <summary>
    /// <see cref="TikConnectionSetup.FromConnectionString"/>: every option the setup has can be said in the string, an
    /// unknown key is refused, and the transport travels with it.
    /// </summary>
    [TestClass]
    public class TikConnectionStringTests
    {
        [TestMethod]
        public void EveryKeyReachesItsOption()
        {
            var setup = TikConnectionSetup.FromConnectionString(
                "transport=ApiSsl;host=192.0.2.1;routerMac=AA:BB:CC:DD:EE:FF;user=admin;password=\"p;a=ss\";port=8730;"
                + "connectTimeout=7;receiveTimeout=00:00:11;sendTimeout=13.5;encoding=us-ascii;allowInvalidCertificate=true;"
                + "cancellationMode=AbandonAndClose;cliReadPageSize=37;cliFieldSeparator=#|#;validateWrites=yes;"
                + "sendTagWithSyncCommand=false;debug=true");

            Assert.AreEqual(TikConnectionType.ApiSsl, setup.ConnectionType);
            Assert.AreEqual("192.0.2.1", setup.Host);
            Assert.AreEqual("AA:BB:CC:DD:EE:FF", setup.Address.Mac);
            Assert.AreEqual("admin", setup.User);
            Assert.AreEqual("p;a=ss", setup.Password, "a quoted value keeps its ; and =");
            Assert.AreEqual(8730, setup.Port);
            Assert.AreEqual(TimeSpan.FromSeconds(7), setup.ConnectTimeout, "a number is seconds");
            Assert.AreEqual(TimeSpan.FromSeconds(11), setup.ReceiveTimeout, "or a TimeSpan");
            Assert.AreEqual(TimeSpan.FromSeconds(13.5), setup.SendTimeout);
            Assert.AreEqual(Encoding.ASCII.WebName, setup.Encoding.WebName);
            Assert.IsTrue(setup.AllowInvalidCertificate);
            Assert.AreEqual(TikCancellationMode.AbandonAndClose, setup.CancellationMode);
            Assert.AreEqual(37, setup.CliReadPageSize);
            Assert.AreEqual("#|#", setup.CliFieldSeparator);
            Assert.IsTrue(setup.ValidateWrites);
            Assert.IsFalse(setup.SendTagWithSyncCommand);
            Assert.AreEqual(true, setup.DebugEnabled);
        }

        [TestMethod]
        public void EverySettableOptionHasAKey()
        {
            // A new option on the setup has to be sayable in configuration too — or listed here with the reason not.
            var notInTheString = new[] { nameof(TikConnectionSetup.CertificateValidationCallback) }; // code, not text
            var covered = new[]
            {
                nameof(TikConnectionSetup.ConnectionType), nameof(TikConnectionSetup.Port), nameof(TikConnectionSetup.ConnectTimeout),
                nameof(TikConnectionSetup.ReceiveTimeout), nameof(TikConnectionSetup.SendTimeout), nameof(TikConnectionSetup.Encoding),
                nameof(TikConnectionSetup.AllowInvalidCertificate), nameof(TikConnectionSetup.CancellationMode),
                nameof(TikConnectionSetup.CliReadPageSize), nameof(TikConnectionSetup.CliFieldSeparator),
                nameof(TikConnectionSetup.ValidateWrites), nameof(TikConnectionSetup.SendTagWithSyncCommand),
                nameof(TikConnectionSetup.DebugEnabled), nameof(TikConnectionSetup.RouterMac), nameof(TikConnectionSetup.RomonAgentSetup),
            };
            var settable = typeof(TikConnectionSetup).GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .Where(p => p.SetMethod?.IsPublic == true).Select(p => p.Name);

            CollectionAssert.AreEquivalent(settable.ToArray(), covered.Concat(notInTheString).ToArray());
        }

        [TestMethod]
        public void KeysAreCaseInsensitive_AndHaveTheUsualAliases()
        {
            var setup = TikConnectionSetup.FromConnectionString("ConnectionType=telnet;Server=router.lan;UID=u;PWD=p");

            Assert.AreEqual(TikConnectionType.Telnet, setup.ConnectionType);
            Assert.AreEqual("router.lan", setup.Host);
            Assert.AreEqual("u", setup.User);
            Assert.AreEqual("p", setup.Password);
        }

        [TestMethod]
        public void AMacAloneAddressesAMacLayerRouter_AndAMissingPasswordIsEmpty()
        {
            var setup = TikConnectionSetup.FromConnectionString("transport=MacTelnet;mac=AA:BB:CC:DD:EE:FF;user=admin");

            Assert.IsNull(setup.Host);
            Assert.AreEqual("AA:BB:CC:DD:EE:FF", setup.Address.Mac);
            Assert.AreEqual(string.Empty, setup.Password);
        }

        [TestMethod]
        public void RomonKeysDescribeTheAgent_AndHostIsTheTargetsId()
        {
            var setup = TikConnectionSetup.FromConnectionString(
                "transport=Ssh;host=AA:BB:CC:DD:EE:01;user=t;password=tp;romon.host=192.0.2.9;romon.user=a;romon.password=ap;romon.port=2222");

            Assert.AreEqual("AA:BB:CC:DD:EE:01", setup.Address.RomonId);
            Assert.AreEqual("192.0.2.9", setup.RomonAgentSetup!.Address.Host);
            Assert.AreEqual("a", setup.RomonAgentSetup.User);
            Assert.AreEqual("ap", setup.RomonAgentSetup.Password);
            Assert.AreEqual(2222, setup.RomonAgentSetup.Port);
        }

        [DataTestMethod]
        [DataRow("host=r;user=u;tranport=Api", "tranport")]              // a typo must not open with a default
        [DataRow("host=r;user=u;transport=Carrier", "transport")]
        [DataRow("host=r;user=u;transport=3", "transport")]              // a number is not a transport name
        [DataRow("host=r;user=u;port=eighty", "port")]
        [DataRow("host=r;user=u;debug=maybe", "debug")]
        [DataRow("host=r;user=u;connectTimeout=-1", "connectTimeout")]
        [DataRow("host=r;user=u;encoding=klingon", "encoding")]
        [DataRow("host=r;password=p", "user")]
        [DataRow("user=u", "host")]
        [DataRow("host=r;user=u;server=s", "host")]                      // one key under two names
        [DataRow("host=r;user=u;romon.user=a", "romon.host")]
        public void AStringThatCannotBeReadIsRefused(string connectionString, string named)
        {
            var ex = Assert.ThrowsException<ArgumentException>(() => TikConnectionSetup.FromConnectionString(connectionString));
            StringAssert.Contains(ex.Message, named);
        }

        [TestMethod]
        public void TheTransportCanBeLeftToTheString_OrNamedAtTheCall()
        {
            var fromString = TikConnectionSetup.FromConnectionString("transport=Rest;host=192.0.2.1;user=u");
            using (var connection = fromString.CreateUnopened())
                Assert.IsInstanceOfType(connection, typeof(ITikRestConnection));
            using (var connection = fromString.CreateUnopened(TikConnectionType.Api))
                Assert.IsInstanceOfType(connection, typeof(ITikApiConnection), "an explicit transport wins");

            var noTransport = TikConnectionSetup.FromConnectionString("host=192.0.2.1;user=u");
            var ex = Assert.ThrowsException<InvalidOperationException>(() => noTransport.CreateUnopened());
            StringAssert.Contains(ex.Message, "transport");
        }

        [TestMethod]
        public void ToStringMasksThePasswords_AndReadsBack()
        {
            var setup = TikConnectionSetup.FromConnectionString(
                "transport=Telnet;host=AA:BB:CC:DD:EE:01;user=t;password=secret1;romon.host=192.0.2.9;romon.user=a;romon.password=secret2");

            string text = setup.ToString();

            Assert.IsFalse(text.Contains("secret"), text);
            StringAssert.Contains(text, "password=***");
            var back = TikConnectionSetup.FromConnectionString(text);
            Assert.AreEqual(setup.ConnectionType, back.ConnectionType);
            Assert.AreEqual(setup.Address.RomonId, back.Address.RomonId);
            Assert.AreEqual(setup.RomonAgentSetup!.Address.Host, back.RomonAgentSetup!.Address.Host);
        }
    }
}
