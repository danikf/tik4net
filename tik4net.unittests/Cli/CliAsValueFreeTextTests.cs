#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using tik4net.Cli;

namespace tik4net.unittests.Cli
{
    /// <summary>
    /// RouterOS 6 has no <c>:serialize</c>, so a script is read through <c>print as-value</c>, which escapes nothing: a source
    /// line holding a <c>=</c> puts its text in front of the <c>=</c> where a field name stands. Text that is no field name
    /// after a field has been read is that field's value going on, not a field of its own — and not the router's complaint.
    /// </summary>
    [TestClass]
    public class CliAsValueFreeTextTests
    {
        // ':put [/system script print detail as-value where name="t4n-probe-ml"]' on 6.49.21, the source being
        // ":local a 1\r\n:local b \"x=y\"\r\n:put $a\r\n".
        private const string MultiLineScriptRow =
            ".id=*1;comment=;dont-require-permissions=false;name=t4n-probe-ml;owner=admin;policy=ftp;reboot;read;write;"
            + "policy;test;password;sniff;sensitive;romon;run-count=0;source=:local a 1\r\n:local b \"x=y\"\r\n:put $a\r\n";

        [TestMethod]
        public void AMultiLineSourceWithAnEquals_IsOneRecordOfFieldNames()
        {
            var rows = CliOutputParser.ParseAsValue(MultiLineScriptRow);

            Assert.IsTrue(CliOutputParser.AreRecords(rows), "a script row was taken for the router's complaint");
            Assert.AreEqual(1, rows.Count);
            Assert.AreEqual("t4n-probe-ml", rows[0].GetResponseField("name"));
            Assert.AreEqual("0", rows[0].GetResponseField("run-count"));
            StringAssert.Contains(rows[0].GetResponseField("source"), ":local b \"x=y\"");
        }

        [TestMethod]
        public void TheFieldAfterTheSource_IsStillAField()
        {
            // A menu whose free text is not its last field: what follows the value is read as its own field again.
            var rows = CliOutputParser.ParseAsValue(".id=*1;on-event=:put 1\r\n:local b \"x=y\";owner=admin");

            Assert.IsTrue(CliOutputParser.AreRecords(rows), "a scheduler row was taken for the router's complaint");
            Assert.AreEqual(1, rows.Count);
            Assert.AreEqual(":put 1;;:local b \"x=y\"", rows[0].GetResponseField("on-event"),
                "as-value has no escape, so the line break reads as a separator; the text is the value's own");
            Assert.AreEqual("admin", rows[0].GetResponseField("owner"));
        }

        [DataTestMethod]
        [DataRow("failure: already have such address=1.2.3.4")]
        [DataRow("input does not match any value of value-name=x")]
        public void AComplaint_IsStillNoRecord(string complaint)
        {
            Assert.IsFalse(CliOutputParser.AreRecords(CliOutputParser.ParseAsValue(complaint)));
        }

        private sealed class Router : ScriptedCliConnectionBase
        {
            internal Router(Func<string, string> answer)
            {
                OpenScripted(_ => Task.FromResult(0),
                    (line, ct) => Task.FromResult(answer(line)),
                    (raw, ct) => Task.FromResult(string.Empty), () => { });
            }

            public override void Open(string host, string user, string password) { }
            public override void Open(string host, int port, string user, string password) { }
            public override Task OpenAsync(string host, string user, string password, CancellationToken cancellationToken = default) => Task.FromResult(0);
            public override Task OpenAsync(string host, int port, string user, string password, CancellationToken cancellationToken = default) => Task.FromResult(0);
        }

        [TestMethod]
        public void AWholeTableReadOfAMultiLineScript_ReturnsTheRow()
        {
            using var router = new Router(line => line.StartsWith(":local d [")
                ? MultiLineScriptRow + "\r\n#n=1/num"
                : ".id=*1;disabled=false");

            var row = router.CreateCommand("/system/script/print").ExecuteList().Single();

            Assert.AreEqual("t4n-probe-ml", row.GetResponseField("name"));
        }
    }
}
