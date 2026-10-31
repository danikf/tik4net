// WinboxNativeTrapSiteTests.cs — the mistakes WinBox native refuses before sending (WinBox review D7), each reaching the
// caller as the exception type the binary API raises for the same mistake.
//
// Measured over the API on 7.25 (the trap messages carry no category, so each is the base TikCommandTrapException):
//   /ip/firewall/filter/set .id=… src-address=999.1.1.1   → "value of range expects range of ip addresses"
//   /ip/address/set .id=… address=999.1.1.1               → "invalid value for argument address"
//   /ip/firewall/filter/unset .id=… value-name=nosuchfield → "input does not match any value of value-name"
//   /ip/firewall/filter/unset .id=…                        → "missing =value-name="
//   /tool/torch interface=… duration=abc                   → "invalid time value for argument duration"
//   /ip/address/set .id=… nosuchfield=1 (and add, and /interface/monitor-traffic … nosuchfield=1)
//                                                          → "unknown parameter nosuchfield"
//   /ip/address/set .id=… dynamic=no                       → "unknown parameter dynamic"
// Router-free: the real connection over a scripted channel.

using System;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using tik4net.Winbox;

namespace tik4net.unittests.Winbox
{
    [TestClass]
    [DoNotParallelize]
    public class WinboxNativeTrapSiteTests
    {
        // A list window with the two IPv4-only shapes: a range network (the firewall's Src. Address, opt/not wrapped)
        // and a plain ipaddr.
        private const string Window =
            "[{name:'X',c:[{name:'Y',title:'Y',type:'map',path:[ 90,1 ],c:[" +
            "{name:'Name',type:'string',id:'s10006'}," +
            "{name:'Src. Address',type:'opt',id:'b1a2',c:[{type:'not',id:'bc8',c:[{type:'network',id:'u32',maskid:'u33',range:1}]}]}," +
            "{name:'Gateway',type:'ipaddr',id:'u5'}]}]}]";

        private static void AssertRefusedBeforeSending(Action<NativeConnectionHarness> act, string messagePart)
        {
            using (var h = new NativeConnectionHarness(Window))
            {
                var ex = Assert.ThrowsException<TikCommandTrapException>(() => act(h));
                Assert.AreEqual(typeof(TikCommandTrapException), ex.GetType(), "the API's trap carries no category: the base type");
                StringAssert.Contains(ex.Message, messagePart);
                Assert.AreEqual(0, h.RequestsWithCommand(WinboxM2Protocol.Command.Set).Count, "nothing was written");
            }
        }

        [TestMethod]
        public void ANonIpv4ValueOnARangeNetworkField_IsRefused()
        {
            // It went out as str:999.1.1.1 on the u32 key; the router took it and the rule printed src-address="".
            AssertRefusedBeforeSending(h => h.Connection.CreateCommandAndParameters("/x/y/set",
                ".id", "*1", "src-address", "999.1.1.1").ExecuteNonQuery(), "invalid value for argument src-address");
        }

        [TestMethod]
        public void ANonIpv4ValueOnAnIpaddrField_IsRefused()
        {
            AssertRefusedBeforeSending(h => h.Connection.CreateCommandAndParameters("/x/y/set",
                ".id", "*1", "gateway", "999.1.1.1").ExecuteNonQuery(), "invalid value for argument gateway");
        }

        [TestMethod]
        public void AnIpv4ValueStillEncodes()
        {
            using (var h = new NativeConnectionHarness(Window))
            {
                h.Connection.CreateCommandAndParameters("/x/y/set", ".id", "*1", "src-address", "10.0.0.0/8").ExecuteNonQuery();
                var set = h.RequestsWithCommand(WinboxM2Protocol.Command.Set).Single();
                Assert.AreEqual(M2Type.U32, set[0x32].Type);
            }
        }

        [TestMethod]
        public void AnUnsetNamingAFieldTheWindowLacks_IsAPlainTrap()
            => AssertRefusedBeforeSending(h => h.Connection.CreateCommandAndParameters("/x/y/unset",
                ".id", "*1", "value-name", "nosuchfield").ExecuteNonQuery(), "nosuchfield");

        // F13: a name with no M2 key is the API's "unknown parameter"; the resolution exception, which says how to add a
        // FieldOverride when the gap is ours, is underneath.
        private static void AssertUnknownParameter(string verb, string field, params string[] more)
        {
            var args = new System.Collections.Generic.List<string>(more) { field, "1" };
            AssertRefusedBeforeSending(h => h.Connection.CreateCommandAndParameters("/x/y/" + verb, args.ToArray())
                .ExecuteNonQuery(), "unknown parameter " + field);
            using (var h = new NativeConnectionHarness(Window))
            {
                var ex = Assert.ThrowsException<TikCommandTrapException>(() => h.Connection
                    .CreateCommandAndParameters("/x/y/" + verb, args.ToArray()).ExecuteNonQuery());
                Assert.AreEqual("unknown parameter " + field, ex.Message);
                Assert.IsInstanceOfType(ex.InnerException, typeof(WinboxFieldResolutionException));
                Assert.AreEqual(0, h.RequestsWithCommand(WinboxM2Protocol.Command.Add).Count, "nothing was added");
            }
        }

        [TestMethod]
        public void ASetNamingAFieldTheWindowLacks_IsTheApisUnknownParameter()
            => AssertUnknownParameter("set", "nosuchfield", ".id", "*1");

        [TestMethod]
        public void AnAddNamingAFieldTheWindowLacks_IsTheApisUnknownParameter()
            => AssertUnknownParameter("add", "nosuchfield", "name", "a");

        [TestMethod]
        public void ASetOfARowState_IsTheApisUnknownParameter()
            => AssertUnknownParameter("set", "dynamic", ".id", "*1");

        // F14: the window's read-only mark. The API refuses such a name as an unknown parameter (actual-interface on
        // /ip/address, 7.25); native answered success and sent nothing.
        private const string MarkedWindow =
            "[{name:'System',c:[{name:'Script',title:'Script',type:'map',path:[ 48,101 ],c:[" +
            "{name:'Name',type:'string',id:'s10006'}," +
            "{name:'Owner',type:'string',id:'s67',ro:1}," +
            "{name:'Run Count',type:'number',id:'u68',ro:1}]}]}]";

        [TestMethod]
        public void ASetOfAReadOnlyField_IsTheApisUnknownParameter()
        {
            using (var h = new NativeConnectionHarness(MarkedWindow))
            {
                var ex = Assert.ThrowsException<TikCommandTrapException>(() => h.Connection
                    .CreateCommandAndParameters("/system/script/set", ".id", "*1", "run-count", "5").ExecuteNonQuery());
                Assert.AreEqual("unknown parameter run-count", ex.Message);
                Assert.AreEqual(typeof(TikCommandTrapException), ex.GetType());
                Assert.IsInstanceOfType(ex.InnerException, typeof(WinboxFieldResolutionException));
                Assert.AreEqual(0, h.RequestsWithCommand(WinboxM2Protocol.Command.Set).Count, "nothing was written");
            }
        }

        [TestMethod]
        public void AnAddOfAReadOnlyField_IsTheApisUnknownParameter()
        {
            using (var h = new NativeConnectionHarness(MarkedWindow))
            {
                var ex = Assert.ThrowsException<TikCommandTrapException>(() => h.Connection
                    .CreateCommandAndParameters("/system/script/add", "name", "a", "run-count", "5").ExecuteNonQuery());
                Assert.AreEqual("unknown parameter run-count", ex.Message);
                Assert.AreEqual(0, h.RequestsWithCommand(WinboxM2Protocol.Command.Add).Count, "nothing was added");
            }
        }

        // The other side of the mark: the API's set takes /system/script owner, and RouterOS honours it over M2 (7.25).
        [TestMethod]
        public void AMarkedFieldTheRouterTakes_IsSent()
        {
            using (var h = new NativeConnectionHarness(MarkedWindow))
            {
                h.Connection.CreateCommandAndParameters("/system/script/set", ".id", "*1", "owner", "bob").ExecuteNonQuery();
                var set = h.RequestsWithCommand(WinboxM2Protocol.Command.Set).Single();
                Assert.AreEqual(M2Type.String, set[0x67].Type);
            }
        }

        // A snapshot monitor reads its parent's row, so nothing looked at its arguments.
        // /interface/monitor-traffic … nosuchfield=1 → "unknown parameter nosuchfield" (7.25).
        [TestMethod]
        public void ASnapshotMonitorNamingAnArgumentItLacks_IsTheApisUnknownParameter()
        {
            using (var h = new NativeConnectionHarness(Window))
            {
                var ex = Assert.ThrowsException<TikCommandTrapException>(() => h.Connection
                    .CreateCommandAndParameters("/x/y/monitor", "numbers", "a", "once", "", "nosuchfield", "1").ExecuteList());
                Assert.AreEqual("unknown parameter nosuchfield", ex.Message);
            }
        }

        [TestMethod]
        public void AnUnsetNamingNoField_IsAPlainTrap()
            => AssertRefusedBeforeSending(h => h.Connection.CreateCommandAndParameters("/x/y/unset",
                ".id", "*1").ExecuteNonQuery(), "value-name");
    }
}
