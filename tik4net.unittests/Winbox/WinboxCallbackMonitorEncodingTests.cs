// WinboxCallbackMonitorEncodingTests.cs — the callback monitor (ExecuteWithCallback) encodes its inputs under the
// read method's rules (WinBox + MAC review, §5 X4).
//
// The read method drops the snapshot modifiers (once, duration — instructions to the client, not inputs of the
// window) and takes a Filter-format parameter as an input, because the read path rewrites the caller's parameters to
// Filter. The callback path did neither: duration= reached the resolver as a field and failed resolution.

#nullable enable
using System;
using System.Linq;
using System.Threading;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using tik4net.Connection;
using tik4net.Winbox;

namespace tik4net.unittests.Winbox
{
    [TestClass]
    [DoNotParallelize]
    public class WinboxCallbackMonitorEncodingTests
    {
        private const int StartCmd = 0xFE0012;
        private const int CancelCmd = 0xFE0013;

        // A ping window (a query monitor) cut down to its two inputs.
        private static readonly string PingWindow =
            "[{name:'Tools',c:[{name:'Ping',c:[{name:'Ping',title:'Ping',type:'query',path:[ 24,1 ],startcmd:" + StartCmd
            + ",cancelcmd:" + CancelCmd + ",autorefresh:1000,c:[" +
            "{name:'Address',type:'string',id:'s1',ro:1},{name:'Count',type:'number',id:'u2',ro:1}]}]}]}]";

        private static System.Collections.Generic.Dictionary<int, Tuple<string, object>> RunCallback(
            params ITikCommandParameter[] parameters)
        {
            using (var h = new NativeConnectionHarness(PingWindow))
            {
                // The start answers a monitor id; every poll ends the pass as Finished, so the worker stops by itself.
                h.Answer = req => NativeConnectionHarness.CommandOf(req) == StartCmd
                    ? NativeConnectionHarness.ReplyTo(req, M2Message.U32Sys(WinboxM2Protocol.RecordKey.Id, 3))
                    : NativeConnectionHarness.ReplyTo(req, M2Message.BoolSys(WinboxM2Protocol.RecordKey.Finished, true));

                ITikTrapSentence? trap = null;
                using (var done = new ManualResetEventSlim())
                {
                    var cmd = h.Connection.CreateCommand("/ping", parameters);
                    // An error ends the run without onDone (D21), so either ending releases the wait.
                    cmd.ExecuteWithCallback(_ => { }, error => { trap = error; done.Set(); }, () => done.Set());
                    Assert.IsTrue(done.Wait(5000), "the monitor must end on the router's Finished; sent " + string.Join(" / ", h.Requests.Select(r => string.Join(",", r.Select(kv => kv.Key.ToString("X") + "=" + kv.Value.Item2)))));
                }
                Assert.IsNull(trap, "the callback monitor must not fail: " + trap?.Message);
                return h.Requests.Single(r => r.TryGetValue(WinboxM2Protocol.SysKey.Command, out var c)
                                              && Convert.ToInt64(c.Item2) == StartCmd);
            }
        }

        [TestMethod]
        public void X4_Duration_IsNotSentAsAnInput()
        {
            var start = RunCallback(
                new TikCommandParameter("address", "192.168.4.1"),
                new TikCommandParameter("count", "3"),
                new TikCommandParameter("duration", "5"));

            Assert.AreEqual("192.168.4.1", start[0x1].Item2);
            Assert.AreEqual(3L, Convert.ToInt64(start[0x2].Item2));
            Assert.AreEqual(2, start.Keys.Count(k => k < 0x10000), "address and count, nothing for duration");
        }

        [TestMethod]
        public void X4_AFilterFormatParameter_IsAnInput()
        {
            var start = RunCallback(
                new TikCommandParameter("address", "192.168.4.1", TikCommandParameterFormat.Filter),
                new TikCommandParameter("count", "3"));

            Assert.AreEqual("192.168.4.1", start[0x1].Item2, "a Filter-format input is the monitor's input, as on the read path");
        }
    }
}
