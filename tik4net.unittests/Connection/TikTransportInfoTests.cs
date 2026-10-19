using System;
using System.Collections.Generic;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using tik4net.Connection;
using tik4net.Ssh;

namespace tik4net.unittests.Connection
{
    /// <summary>
    /// The facts about each built-in transport (<see cref="TikTransportInfo"/>), and that every connection the registry
    /// builds is the type its row describes: the name in its messages, and the agent type it reports through a RoMON
    /// relay.
    /// </summary>
    [TestClass]
    public class TikTransportInfoTests
    {
        // The name each transport gives itself in "… connection is not open." and every other message that names it.
        private static readonly Dictionary<TikConnectionType, string> Names = new Dictionary<TikConnectionType, string>
        {
            [TikConnectionType.Api] = "API",
            [TikConnectionType.ApiSsl] = "API",
            [TikConnectionType.Rest] = "REST",
            [TikConnectionType.RestSsl] = "REST",
            [TikConnectionType.Ssh] = "SSH",
            [TikConnectionType.Telnet] = "Telnet",
            [TikConnectionType.MacTelnet] = "MAC-Telnet",
            [TikConnectionType.WinboxCli] = "WinBox CLI",
            [TikConnectionType.WinboxCliMac] = "WinBox CLI MAC",
            [TikConnectionType.WinboxNative] = "WinBox native",
            [TikConnectionType.WinboxNativeMac] = "WinBox native MAC",
        };

        private static ITikConnection Create(TikConnectionType type)
        {
            if (type == TikConnectionType.Ssh)
                Tik4NetSsh.Register();
            return TikConnectionRegistry.Create(type);
        }

        [TestMethod]
        public void EveryDeclaredTransportHasANameHere()
            => CollectionAssert.AreEquivalent(Enum.GetValues(typeof(TikConnectionType)), new List<TikConnectionType>(Names.Keys));

        /// <summary>The registry is the factory and the table the facts: each connection it builds is its row's type.</summary>
        [TestMethod]
        public void EveryBuiltInConnectionIsTheTypeOfItsRow()
        {
            foreach (TikConnectionType type in Enum.GetValues(typeof(TikConnectionType)))
                using (var connection = Create(type))
                {
                    Assert.AreEqual(type, ((TikConnectionBase)connection).ConnectionType, type.ToString());
                    Assert.AreEqual(type, TikTransportInfo.Of(type).Type);
                    Assert.AreEqual(Names[type], TikTransportInfo.Of(type).DisplayName, type.ToString());
                    Assert.AreEqual(connection is ITikMacLayerConnection, TikTransportInfo.Of(type).MacLayer, type + ": MAC layer");
                    Assert.AreEqual(connection is ITikRomonConnection, TikTransportInfo.Of(type).RelaysRomon, type + ": relays RoMON");
                }
        }

        [TestMethod]
        public void EachTransportNamesItselfInTheNotOpenMessage()
        {
            foreach (var pair in Names)
                using (var connection = Create(pair.Key))
                {
                    var ex = Assert.ThrowsException<TikConnectionNotOpenException>(
                        () => connection.CreateCommand("/system/identity/print").ExecuteList(), pair.Key.ToString());
                    Assert.AreEqual(pair.Value + " connection is not open.", ex.Message, pair.Key.ToString());
                }
        }

        [TestMethod]
        public void EachRelayingTransportReportsItselfAsTheAgent()
        {
            foreach (TikConnectionType type in Enum.GetValues(typeof(TikConnectionType)))
                using (var connection = Create(type))
                    if (connection is ITikRomonConnection romon)
                        Assert.AreEqual(type, romon.RomonAgentConnectionType, type.ToString());
        }
    }
}
