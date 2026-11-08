#nullable enable
using System;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using tik4net.Objects;
using tik4net.Objects.Tool;
using tik4net.Testing;

namespace tik4net.unittests.Objects
{
    /// <summary>
    /// <see cref="MacAddress"/> holds a MAC the caller gave it; no text is turned into the zero MAC, and Wake-on-LAN refuses
    /// a missing MAC before anything is sent.
    /// </summary>
    [TestClass]
    public class MacAddressTests
    {
        [TikEntity("/test/mac")]
        private class MacEntity
        {
            [TikProperty("mac")] public TikField<MacAddress?> Mac { get; set; }
        }

        [DataTestMethod]
        [DataRow(null)]
        [DataRow("")]
        [DataRow("   ")]
        public void NoTextIsNotAMac(string? text)
        {
            Assert.ThrowsException<ArgumentException>(() => new MacAddress(text!));
        }

        [TestMethod]
        public void TheParameterlessConstructorIsTheExplicitZeroMac()
        {
            Assert.AreEqual("00:00:00:00:00:00", new MacAddress().Address);
        }

        [TestMethod]
        public void ANullInstanceConvertsToANullString()
        {
            MacAddress? mac = null;
            string? text = mac!;
            Assert.IsNull(text);
        }

        [DataTestMethod]
        [DataRow(null)]
        [DataRow("")]
        [DataRow("   ")]
        public void WakeOnLanRefusesAMissingMacBeforeSendingAnything(string? text)
        {
            var connection = new TikFakeConnection();

            Exception? thrown = null;
            try { connection.ExecuteWol(text!); }
            catch (Exception ex) { thrown = ex; }

            Assert.AreEqual(text == null ? typeof(ArgumentNullException) : typeof(ArgumentException), thrown?.GetType());
            Assert.AreEqual(0, connection.SentCommands.Count);
        }

        [TestMethod]
        public void WakeOnLanRefusesANullMacAddress()
        {
            var connection = new TikFakeConnection();

            Assert.ThrowsException<ArgumentNullException>(() => connection.ExecuteWol((MacAddress)null!));
            Assert.AreEqual(0, connection.SentCommands.Count);
        }

        [TestMethod]
        public void WakeOnLanSendsTheGivenMac()
        {
            var connection = new TikFakeConnection().WithNonQuery(rows => rows.First() == "/tool/wol");

            connection.ExecuteWol("AA:BB:CC:DD:EE:FF");

            Assert.AreEqual(1, connection.SentCommands.Count);
            CollectionAssert.Contains(connection.SentCommands.Single(), "=mac=AA:BB:CC:DD:EE:FF");
        }

        [DataTestMethod]
        [DataRow("")]
        [DataRow("not-a-mac")]
        public void AReadNeverThrowsOnAWordThatIsNotAMac(string word)
        {
            var accessor = TikEntityMetadataCache.GetMetadata<MacEntity>().Properties.Single(p => p.FieldName == "mac");
            var entity = new MacEntity();

            accessor.SetEntityValue(entity, word);

            Assert.AreNotEqual(TikFieldState.Present, entity.Mac.State, word);
        }
    }
}
