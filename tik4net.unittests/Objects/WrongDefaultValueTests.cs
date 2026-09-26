// WrongDefaultValueTests.cs — an add leaves out a plain property equal to its DefaultValue, so a DefaultValue that is
// not the router's real default silently drops what the caller asked for: StoreOnDisk = false was never sent, and the
// router applied its own default, true. Corrected against a freshly created row on RouterOS 7.24 and the WinBox
// catalog (the 5.0 DefaultValue audit).

using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using tik4net.Objects;
using tik4net.Objects.Interface;
using tik4net.Objects.Interface.Vpn;
using tik4net.Objects.Ip.Hotspot;
using tik4net.Objects.Tool.Graphing;
using tik4net.Testing;

namespace tik4net.unittests.Objects
{
    [TestClass]
    public class WrongDefaultValueTests
    {
        private static string[] SentAdd<T>(T entity, string path) where T : new()
        {
            var connection = new TikFakeConnection().WithScalarResponse(cmd => cmd.First() == path + "/add", "*1");
            connection.Save(entity);
            return connection.SentCommands.Single(c => c.First() == path + "/add").ToArray();
        }

        [TestMethod]
        public void GraphingStoreOnDiskFalse_IsSent()
            => CollectionAssert.Contains(SentAdd(new GraphingInterface { Interface = "ether1", StoreOnDisk = false }, "/tool/graphing/interface"),
                "=store-on-disk=no", "the router defaults store-on-disk to yes");

        [TestMethod]
        public void PppoeClientKeepaliveTimeout60_IsSent()
            => CollectionAssert.Contains(SentAdd(new InterfacePppoeClient { Name = "p", Interface = "ether1", KeepaliveTimeout = 60 }, "/interface/pppoe-client"),
                "=keepalive-timeout=60", "the router defaults keepalive-timeout to 10");

        [TestMethod]
        public void OvpnClientUsePeerDnsNo_IsSent()
            => CollectionAssert.Contains(SentAdd(new OvpnClient { Name = "o", ConnectTo = "192.0.2.1", UsePeerDns = false }, "/interface/ovpn-client"),
                "=use-peer-dns=no", "the router defaults use-peer-dns to yes");

        [TestMethod]
        public void HotspotUserProfileSharedUsersUnlimited_IsSent()
            => CollectionAssert.Contains(SentAdd(new HotspotUserProfile { Name = "h", SharedUsers = "unlimited" }, "/ip/hotspot/user/profile"),
                "=shared-users=unlimited", "the router defaults shared-users to 1");
    }
}
