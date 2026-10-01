using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using tik4net.Objects;
using tik4net.Objects.Tool;

namespace tik4net.integrationtests
{
    [TestClass]
    [SafeInParallelLegs]
    public class TorchTest : TestBase
    {
        [TestMethod]
        public void TorchWillNotFail()
        {
            EnsureCapability(TikConnectionCapability.Listen, "Torch");
            bool isFailed = false;
            int rowCount = 0, firstRows = 0, secondRows = 0;
            Connection.OnWriteRow += (sender, args) => { System.Diagnostics.Debug.WriteLine(args.Word); };
            Connection.OnReadRow += (sender, args) => { System.Diagnostics.Debug.WriteLine(args.Word); };

            var cmd1 = Connection.LoadWithCallback<ToolTorch>(t => { Interlocked.Increment(ref rowCount); Interlocked.Increment(ref firstRows); System.Diagnostics.Debug.WriteLine("ether1a: " + t); },
                ex => { System.Diagnostics.Debug.WriteLine("ERROR: " + ex.Message); isFailed = true; },
                Connection.CreateParameter("interface", TestConstants.Interface));
            var cmd2 = Connection.LoadWithCallback<ToolTorch>(t => { Interlocked.Increment(ref rowCount); Interlocked.Increment(ref secondRows); System.Diagnostics.Debug.WriteLine("ether1b: " + t); },
                ex => { System.Diagnostics.Debug.WriteLine("ERROR: " + ex.Message); isFailed = true; },
                Connection.CreateParameter("interface", TestConstants.Interface));
            // CLI transports drive torch via freeze-frame-interval, where each poll blocks for several real
            // seconds (see CliConnectionBase.TorchFreezeFrameSeconds) — much slower than the binary API's
            // near-instant streaming poll, and the two concurrent commands may serialize on one channel.
            // Give both time to complete at least one full cycle before cancelling either.
            // The budgets are the fixed sleeps this used to take; the waits end as soon as the rows are there.
            var settle = TimeSpan.FromMilliseconds(Connection.Supports(TikConnectionCapability.Streaming) ? 1500 : 9000);
            WaitUntil(() => Volatile.Read(ref firstRows) > 0 && Volatile.Read(ref secondRows) > 0, settle);
            cmd2.CancelAndJoin();
            int before = Volatile.Read(ref firstRows);
            WaitUntil(() => Volatile.Read(ref firstRows) > before, settle);   // the first torch outlives the second
            cmd1.CancelAndJoin();

            Assert.IsFalse(isFailed);
            Assert.IsTrue(rowCount > 0, "Expected at least one torch row, got " + rowCount);
        }
    }
}
