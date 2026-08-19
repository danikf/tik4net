// G2MaxObjsProbeTest.cs — probe: what does the router do with ufe0018 (maxObjs) on a getall?
//
// G2 was written as "paging (maxObjs with the cursor) is the real fix". The .jg says something else:
// maxobjs is a CAP, declared on three windows only (routes, connections, proxy cache) and paired with a
// maxobjsmsg — "There are too many records to show them all" — i.e. a refusal, not a page boundary. Those
// are two different things, so the router was asked rather than either source believed.
//
// ANSWER (RouterOS 7.23.2, /log = handler [3,4], ~1000 rows), maxObjs 0 / 10 / 50 / 200 / 10000:
//     all five identical — 1000 rows in 5 pages of 208/201/209/205/177.
// The router picks the page size and ignores ufe0018 for it. There is no page-size knob, so the client
// cannot make a read proportional to the answer instead of the table; what it can do is refuse to return a
// truncated table silently, which is what GetAllAsync's budget now does.
//
// [Ignore] keeps it out of the matrix — run via --filter.

using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Collections.Generic;
using System.Configuration;
using System.Diagnostics;
using tik4net.Winbox;

namespace tik4net.integrationtests
{
    [Ignore("G2 probe — hits a live router and reads the whole log. Remove the attribute to run.")]
    [TestClass]
    public class G2MaxObjsProbeTest
    {
        private const int WINBOX_PORT = 8291;
        private static readonly int[] LOG = { 3, 4 };   // .jg: name:'Log Entry', path:[ 3,4 ]

        private static (string host, string user, string pass) Cfg() => (
            ConfigurationManager.AppSettings["host"],
            ConfigurationManager.AppSettings["user"],
            ConfigurationManager.AppSettings["pass"] ?? "");

        [TestMethod]
        public void MaxObjs_PageSizeOrCap()
        {
            var (host, user, pass) = Cfg();

            using (var client = new WinboxM2Client())
            {
                client.Connect(host, WINBOX_PORT);
                client.Authenticate(host, WINBOX_PORT, user, pass);

                foreach (int maxObjs in new[] { 0, 10, 50, 200, 10000 })
                    Console.WriteLine(RunGetAll(client, LOG, maxObjs));
            }
        }

        // The cursor loop, opened up so every ROUND is reported — the page size is the question and a
        // total row count cannot answer it.
        private static string RunGetAll(WinboxM2Client client, int[] handler, int maxObjs)
        {
            var pages = new List<int>();
            object cont = null;
            byte reqId = 40;
            var sw = Stopwatch.StartNew();
            int status = 0;

            for (int round = 0; round < 64; round++)
            {
                var head = new List<byte[]>
                {
                    M2Message.SysToArr(handler), M2Message.SysFrom(),
                    M2Message.BoolSys(WinboxM2Protocol.SysKey.ReplyExpected, true),
                    M2Message.U8Sys(WinboxM2Protocol.SysKey.RequestId, reqId++),
                    M2Message.U32Sys(WinboxM2Protocol.SysKey.Command, WinboxM2Protocol.Command.GetAll),
                    M2Message.U32Sys(WinboxM2Protocol.RecordKey.Flags, WinboxM2Protocol.GetAllFlags),
                };
                if (maxObjs > 0) head.Add(M2Message.U32Sys(WinboxM2Protocol.RecordKey.MaxObjs, maxObjs));
                if (cont != null)
                    head.Add(M2Message.U32Sys(WinboxM2Protocol.RecordKey.Continuation, Convert.ToInt32(cont)));

                client.EncryptAndSendPublic(M2Message.BuildM2(head.ToArray()));
                byte[] resp = client.RecvAndDecryptPublic(30000);

                status = M2Message.ParseSysStatus(resp);
                pages.Add(M2Message.ParseRecords(resp, WinboxM2Protocol.RecordKey.Records).Count);

                if (status != WinboxM2Protocol.Error.None && status != WinboxM2Protocol.Error.ObjectNonexistent)
                    return $"maxObjs={maxObjs,6}: ERROR 0x{status:X} after pages [{string.Join(",", pages)}] "
                         + $"in {sw.ElapsedMilliseconds} ms";
                if (status == WinboxM2Protocol.Error.ObjectNonexistent) break;

                var fields = M2Message.ParseAllFields(resp);
                if (!fields.TryGetValue(WinboxM2Protocol.RecordKey.Continuation, out var ct)) break;
                cont = ct.Item2;
            }

            int total = 0;
            foreach (int p in pages) total += p;
            return $"maxObjs={maxObjs,6}: {total,6} rows in {pages.Count} page(s) {string.Join("/", pages)} "
                 + $"— {sw.ElapsedMilliseconds} ms, final status 0x{status:X}";
        }
    }
}
