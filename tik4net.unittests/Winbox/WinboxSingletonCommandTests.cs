using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using tik4net.Winbox;

namespace tik4net.unittests.Winbox
{
    /// <summary>
    /// A singleton window may name its own read and write commands. webfig's <c>ObjectHolder</c> sends
    /// <c>uff0007 = getcmd || 0xfe000d</c> to read and <c>setcmd || 0xfe000e</c> to write, nothing else differing
    /// (RouterOS 6.49.13 <c>master-min.js</c>); the default command on such a window is refused with 0xFE0003.
    /// </summary>
    [TestClass]
    public class WinboxSingletonCommandTests
    {
        // RouterOS 6.49.13's IP Accounting menu, trimmed: one handler, [46], hosting the settings singleton with
        // its own commands AND the snapshot list, plus the web-access singleton on [50] with commands of its own.
        private const string Accounting =
            "[{name:'Accounting',title:'IP Traffic Accounting',group:'IP',c:[" +
            "{title:'Traffic Accounting',type:'item',path:[ 46 ],getcmd:2,setcmd:1,c:[" +
            "{name:'Enable Accounting',type:'bool',id:'b15'},{name:'Threshold',type:'number',id:'u14'}]}," +
            "{title:'Snapshot',type:'map',path:[ 46 ],ro:1,c:[{name:'Src. Address',type:'ipaddr',id:'u1'}]}," +
            "{name:'Traffic Accounting Web Access',title:'Web Access',type:'item',path:[ 50 ],getcmd:1,setcmd:2,c:[" +
            "{name:'Accessible via Web',type:'bool',id:'b3'}]}]}]";

        private static WinboxJgCatalog Catalog()
        {
            var catalog = new WinboxJgCatalog();
            Assert.IsTrue(catalog.TryParseInto(Accounting), "the trimmed menu must parse");
            return catalog;
        }

        [TestMethod]
        public void AWindowsOwnCommands_AreRecordedAgainstThatWindow()
        {
            var catalog = Catalog();
            Assert.IsTrue(catalog.GetDerivedPaths().ContainsKey("/ip/accounting/traffic-accounting"),
                string.Join(", ", catalog.GetDerivedPaths().Keys));

            catalog.GetSingletonCommands("/ip/accounting/traffic-accounting", out int? get, out int? set);
            Assert.AreEqual(2, get);
            Assert.AreEqual(1, set);

            catalog.GetSingletonCommands("/ip/accounting/traffic-accounting-web-access", out get, out set);
            Assert.AreEqual(1, get);
            Assert.AreEqual(2, set);
        }

        [TestMethod]
        public void TheListOnTheSameHandler_HasNoneOfThem()
        {
            // [46] is the settings singleton AND the snapshot table; the commands belong to the first only.
            Catalog().GetSingletonCommands("/ip/accounting/snapshot", out int? get, out int? set);
            Assert.IsNull(get);
            Assert.IsNull(set);
        }

        [TestMethod]
        public void AnUnknownPath_LeavesTheDefaults()
        {
            Catalog().GetSingletonCommands(null, out int? get, out int? set);
            Assert.IsNull(get);
            Assert.IsNull(set);
        }

        [TestMethod]
        public void TheReadCarriesTheWindowsCommand_OrGetSingleton()
        {
            var ops = new WinboxNativeM2Operations(new NoChannel());

            Assert.AreEqual(2L, Command(ops.BuildGetSingleton(new[] { 46 }, WinboxM2Protocol.GetAllFlags, 2)));
            Assert.AreEqual((long)WinboxM2Protocol.Command.GetSingleton,
                Command(ops.BuildGetSingleton(new[] { 46 }, WinboxM2Protocol.GetAllFlags)));
        }

        [TestMethod]
        public void TheWriteCarriesTheWindowsCommand_OrSetSingleton()
        {
            var ops = new WinboxNativeM2Operations(new NoChannel());

            Assert.AreEqual(1L, Command(ops.BuildSetSingleton(new[] { 46 }, new List<byte[]>(), -1, 1)));
            Assert.AreEqual((long)WinboxM2Protocol.Command.SetSingleton,
                Command(ops.BuildSetSingleton(new[] { 46 }, new List<byte[]>(), -1)));
        }

        private static long Command(byte[] message)
            => Convert.ToInt64(M2Message.ParseAllFields(message)[WinboxM2Protocol.SysKey.Command].Item2);

        /// <summary>Only builds messages here; nothing is ever sent.</summary>
        private sealed class NoChannel : IWinboxM2Channel
        {
            public bool IsEncrypted => true;
            public bool DataAvailable => false;
            public long BytesReceived => 0;
            public bool SupportsStaleDrain => false;
            public bool SendAbandoned => false;
            public bool SendStalled => false;
            public bool SupportsReaderLoop => false;
            public byte[] SendReceive(byte[] m2, int timeoutMs) => throw new NotSupportedException();
            public void Open(string host, int port, string user, string password, int connectTimeoutMs, int ioTimeoutMs, int sendTimeoutMs = 0)
                => throw new NotSupportedException();
            public byte[] NextReqIdField() => new byte[0];
            public void Send(byte[] m2) => throw new NotSupportedException();
            public byte[] Receive(int timeoutMs) => throw new NotSupportedException();
            public byte[] ReceiveNextFrame() => throw new NotSupportedException();
            public void StartIdleServicing() { }
            public void Dispose() { }
        }
    }
}
