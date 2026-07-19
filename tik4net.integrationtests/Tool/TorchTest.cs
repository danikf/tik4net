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
    public class TorchTest : TestBase
    {
        [TestMethod]
        public void TorchWillNotFail()
        {
            EnsureCapability(TikConnectionCapability.Listen, "Torch");
            bool isFailed = false;
            int rowCount = 0;
            Connection.OnWriteRow += (sender, args) => { System.Diagnostics.Debug.WriteLine(args.Word); };
            Connection.OnReadRow += (sender, args) => { System.Diagnostics.Debug.WriteLine(args.Word); };

            var cmd1 = Connection.LoadAsync<ToolTorch>(t => { rowCount++; System.Diagnostics.Debug.WriteLine("ether1a: " + t); },
                ex => { System.Diagnostics.Debug.WriteLine("ERROR: " + ex.Message); isFailed = true; },
                Connection.CreateParameter("interface", TestConstants.Interface));
            var cmd2 = Connection.LoadAsync<ToolTorch>(t => { rowCount++; System.Diagnostics.Debug.WriteLine("ether1b: " + t); },
                ex => { System.Diagnostics.Debug.WriteLine("ERROR: " + ex.Message); isFailed = true; },
                Connection.CreateParameter("interface", TestConstants.Interface));
            // CLI transports drive torch via freeze-frame-interval, where each poll blocks for several real
            // seconds (see CliConnectionBase.TorchFreezeFrameSeconds) — much slower than the binary API's
            // near-instant streaming poll, and the two concurrent commands may serialize on one channel.
            // Give both time to complete at least one full cycle before cancelling either.
            int settleMs = Connection.Supports(TikConnectionCapability.Streaming) ? 1500 : 9000;
            Thread.Sleep(settleMs);
            cmd2.CancelAndJoin();
            Thread.Sleep(settleMs);
            cmd1.CancelAndJoin();

            Assert.IsFalse(isFailed);
            Assert.IsTrue(rowCount > 0, "Expected at least one torch row, got " + rowCount);
        }
    }
}
