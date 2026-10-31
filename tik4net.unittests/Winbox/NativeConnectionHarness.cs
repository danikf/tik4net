#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using tik4net.Winbox;
using tik4net.WinboxNative;

namespace tik4net.unittests.Winbox
{
    /// <summary>
    /// A <see cref="WinboxNativeConnection"/> opened over a scripted M2 channel and a <c>.jg</c> fragment, so a test can
    /// drive the whole command path — <c>ExecuteNonQuery</c>, the writer, the monitor — and read the M2 requests it
    /// sends, with no router.
    /// </summary>
    /// <remarks>
    /// The open is what <c>InitAfterAuth</c> does minus the catalog fetch and the multiplexer: the parsed catalog is
    /// put in place of the one a router would send, and no multiplexer is started, so the operations keep the
    /// open-phase round trip and every request is answered in order by <see cref="Answer"/>. The fields are set by reflection — the connection has no seam for this, and a test
    /// that needed one should not be the reason production code grows it.
    /// </remarks>
    internal sealed class NativeConnectionHarness : IDisposable
    {
        internal readonly ScriptedChannel Channel = new ScriptedChannel();
        internal readonly WinboxNativeConnection Connection;

        internal NativeConnectionHarness(string jgBody)
        {
            var catalog = new WinboxJgCatalog();
            Assert.IsTrue(catalog.TryParseInto(jgBody), "the .jg fragment must parse");

            Connection = new WinboxNativeConnection();
            var ops = new WinboxNativeM2Operations(Channel, 5000);
            Set("_session", Channel);
            Set("_ops", ops);
            Set("_catalog", catalog);
            var map = (WinboxHandlerMap)Get("_handlerMap");
            map.SetDerivedPaths(catalog.GetDerivedPaths());
            map.SetSubtypeFilters(catalog.GetSubtypeFilters());
            var references = new WinboxReferenceTables(ops, catalog);
            Set("_references", references);
            Set("_codec", new WinboxRecordCodec(ops, catalog, references));
            typeof(tik4net.Connection.TikConnectionBase)
                .GetMethod("SetOpened", BindingFlags.Instance | BindingFlags.NonPublic)!
                .Invoke(Connection, null);
        }

        /// <summary>What the channel answers to a request; by default an empty reply carrying its request id.</summary>
        internal Func<byte[], byte[]?> Answer
        {
            get => Channel.Answer;
            set => Channel.Answer = value;
        }

        /// <summary>Every request sent, parsed into key → (type, value).</summary>
        internal List<M2Record> Requests
            => Channel.Sent.Select(M2Message.ParseAllFields).ToList();

        /// <summary>The requests whose <c>SYS_CMD</c> is <paramref name="command"/>.</summary>
        internal List<M2Record> RequestsWithCommand(int command)
            => Requests.Where(r => r.TryGetValue(WinboxM2Protocol.SysKey.Command, out var c)
                                   && Convert.ToInt64(c.Value) == command).ToList();

        /// <summary>A reply to <paramref name="request"/> carrying its request id and <paramref name="fields"/>.</summary>
        internal static byte[] ReplyTo(byte[] request, params byte[][] fields)
        {
            var all = new List<byte[]> { M2Message.SysToArr(), M2Message.SysFrom(24) };
            int? id = M2Message.ParseSysReqId(request);
            if (id.HasValue) all.Add(M2Message.U8Sys(WinboxM2Protocol.SysKey.RequestId, (byte)id.Value));
            all.AddRange(fields);
            return M2Message.BuildM2(all.ToArray());
        }

        /// <summary>A getall reply listing <paramref name="records"/>.</summary>
        internal static byte[] RecordsReply(byte[] request, params byte[][][] records)
            => ReplyTo(request, M2Message.MessageArraySys(WinboxM2Protocol.RecordKey.Records, records));

        internal static int CommandOf(byte[] request)
        {
            var all = M2Message.ParseAllFields(request);
            return all.TryGetValue(WinboxM2Protocol.SysKey.Command, out var c) ? (int)Convert.ToInt64(c.Value) : -1;
        }

        private FieldInfo Field(string name)
            => typeof(WinboxNativeConnection).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)
               ?? throw new InvalidOperationException("WinboxNativeConnection has no field " + name);

        private object Get(string name) => Field(name).GetValue(Connection)!;

        private void Set(string name, object value) => Field(name).SetValue(Connection, value);

        public void Dispose() => Connection.Dispose();

        /// <summary>Records every message sent and answers each with what <see cref="Answer"/> makes of it.</summary>
        internal sealed class ScriptedChannel : IWinboxM2Channel
        {
            private readonly Queue<byte[]> _inbound = new Queue<byte[]>();
            private byte _nextId;
            internal readonly List<byte[]> Sent = new List<byte[]>();
            internal Func<byte[], byte[]?> Answer = req => ReplyTo(req);

            public bool IsEncrypted => true;
            public bool DataAvailable => _inbound.Count > 0;
            public bool SupportsStaleDrain => false;
            public bool SendAbandoned => false;
            public bool SendStalled => false;
            public long BytesReceived => 0;
            public void Open(string host, int port, string user, string password, int connectTimeoutMs, int ioTimeoutMs, int sendTimeoutMs = 0) { }
            public byte[] NextReqIdField() => M2Message.U8Sys(WinboxM2Protocol.SysKey.RequestId, ++_nextId);
            public void Send(byte[] m2)
            {
                Sent.Add(m2);
                byte[]? reply = Answer(m2);
                if (reply != null) _inbound.Enqueue(reply);
            }
            public byte[] Receive(int timeoutMs) => _inbound.Count > 0 ? _inbound.Dequeue() : null!;
            public byte[] SendReceive(byte[] m2, int timeoutMs) { Send(m2); return Receive(timeoutMs); }
            public byte[] ReceiveNextFrame() => throw new NotSupportedException();
            public void StartIdleServicing() { }
            public void Dispose() { }
        }
    }
}
