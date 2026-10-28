// WinboxNativeWriterDefectTests.cs — the writer defects the WinBox + MAC review found by symmetry (§5), each driven
// through the real connection over a scripted channel (NativeConnectionHarness), so what is asserted is the M2
// request the router would have received.

using System;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using tik4net.Winbox;

namespace tik4net.unittests.Winbox
{
    [TestClass]
    [DoNotParallelize]   // the harness answers on the calling thread, but keep it off the shared pool regardless
    public class WinboxNativeWriterDefectTests
    {
        // The generic interface window and the EoIP subtype, as RouterOS 7.23.2 declares them (cut down).
        private const string InterfaceWindows =
            "[{name:'Interfaces',c:[{name:'Interface',title:'Interface',type:'map',path:[ 20,0 ],generic:'iface'," +
            "typeon:'type',c:[{name:'type',type:'number',id:'u10001',ro:1},{name:'Name',type:'string',id:'s10006'}]}," +
            "{name:'Interface',title:'EoIP Tunnel',type:'map',inherit:'iface',typevalue:17,c:[" +
            "{name:'Remote Address',type:'addr',id:'m1'}]}]}]";

        // ── X1 ────────────────────────────────────────────────────────────────

        [TestMethod]
        public void X1_AnInterfaceSubtypeAdd_ThroughExecuteNonQuery_CarriesTheDiscriminator()
        {
            // ExecuteScalar's add appends type=<subtype>; without it the router answers 'unsupported device type'.
            // The same add through ExecuteNonQuery reaches the writer's verb switch, which must do the same.
            using (var h = new NativeConnectionHarness(InterfaceWindows))
            {
                h.Connection.CreateCommandAndParameters("/interface/eoip/add",
                    "name", "eoip-x1", "remote-address", "192.168.4.250").ExecuteNonQuery();

                var add = h.RequestsWithCommand(WinboxM2Protocol.Command.Add).Single();
                Assert.IsTrue(add.ContainsKey(0x10001), "the add must carry the subtype discriminator (type, 0x10001)");
                Assert.AreEqual(17L, Convert.ToInt64(add[0x10001].Item2), "EoIP is interface type 17");
            }
        }

        [TestMethod]
        public void X1_AnInterfaceSubtypeAdd_ThroughExecuteScalar_CarriesTheDiscriminator()
        {
            // The path that already worked — kept beside the fix so the two cannot drift apart again.
            using (var h = new NativeConnectionHarness(InterfaceWindows))
            {
                h.Answer = req => NativeConnectionHarness.ReplyTo(req,
                    M2Message.U32Sys(WinboxM2Protocol.RecordKey.Id, 0x2B));
                string id = h.Connection.CreateCommandAndParameters("/interface/eoip/add",
                    "name", "eoip-x1", "remote-address", "192.168.4.250").ExecuteScalar()!;

                Assert.AreEqual("*2B", id);
                var add = h.RequestsWithCommand(WinboxM2Protocol.Command.Add).Single();
                Assert.AreEqual(17L, Convert.ToInt64(add[0x10001].Item2));
            }
        }
    }
}
