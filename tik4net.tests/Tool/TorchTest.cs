using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using tik4net.Objects;
using tik4net.Objects.Tool;

namespace tik4net.tests
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
            Thread.Sleep(1500);
            cmd2.CancelAndJoin();
            Thread.Sleep(1500);
            cmd1.CancelAndJoin();

            Assert.IsFalse(isFailed);
            Assert.IsTrue(rowCount > 0, "Expected at least one torch row, got " + rowCount);
        }
    }
}
