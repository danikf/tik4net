using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Linq;
using System.Reflection;
using tik4net.Objects;
using tik4net.Objects.Interface;
using tik4net.Objects.Interface.Bridge;
using tik4net.Objects.Ip.DhcpServer;
using tik4net.Objects.Ip.Firewall;
using tik4net.Objects.Routing.Bgp;
using tik4net.Objects.System;

namespace tik4net.unittests.Objects
{
    /// <summary>
    /// A field the router refuses on <c>set</c> and <c>add</c> has no setter a caller can reach, so assigning one fails at
    /// compile time rather than as <c>unknown parameter</c> from <c>Save</c>. The router's side is measured by
    /// <c>EntityReadOnlyFieldTest</c> in the integration suite.
    /// </summary>
    [TestClass]
    public class EntityReadOnlyFieldTests
    {
        [DataTestMethod]
        [DataRow(typeof(InterfaceVlan), nameof(InterfaceVlan.MacAddress))]
        [DataRow(typeof(BridgePort), nameof(BridgePort.ExternalFdb))]
        [DataRow(typeof(DhcpServerLease), nameof(DhcpServerLease.SrcMacAddress))]
        [DataRow(typeof(DhcpServerOption), nameof(DhcpServerOption.RawValue))]
        [DataRow(typeof(FirewalServicePort), nameof(FirewalServicePort.Name))]
#pragma warning disable CS0618 // the entity of RouterOS 6's /routing/bgp/peer
        [DataRow(typeof(BgpPeer), nameof(BgpPeer.RemoteId))]
#pragma warning restore CS0618
        public void TheFieldIsReadOnly(Type entity, string property)
            => AssertReadOnly(entity.GetProperty(property)!);

        [TestMethod]
        public void EveryFieldOfSystemRouterboardIsReadOnly()
        {
            var properties = typeof(SystemRouterboard).GetProperties().Where(p => p.GetCustomAttribute<TikPropertyAttribute>() != null).ToList();
            Assert.AreEqual(8, properties.Count, "a field added here belongs under this test too");
            properties.ForEach(AssertReadOnly);
        }

        [TestMethod]
        public void NtpClientModeStaysWritable_RouterOs7TakesIt()
        {
            // Read-only on 6.49.21 and writable from 7 on: the property has to serve both, and the router answers on 6.
            var property = typeof(SystemNtpClient).GetProperty(nameof(SystemNtpClient.Mode))!;
            Assert.IsFalse(property.GetCustomAttribute<TikPropertyAttribute>()!.IsReadOnly);
            Assert.IsTrue(property.SetMethod!.IsPublic);
        }

        private static void AssertReadOnly(PropertyInfo property)
        {
            string name = property.DeclaringType!.Name + "." + property.Name;
            Assert.IsTrue(property.GetCustomAttribute<TikPropertyAttribute>()!.IsReadOnly, name + " is not IsReadOnly");
            Assert.IsFalse(property.SetMethod!.IsPublic, name + " has a public setter");
        }
    }
}
