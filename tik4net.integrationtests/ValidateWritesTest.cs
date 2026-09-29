using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Collections.Generic;
using System.Linq;
using tik4net.Objects;
using tik4net.Objects.Ip;
using tik4net.Objects.Routing;

namespace tik4net.integrationtests
{
    /// <summary>
    /// <see cref="TikConnectionSetup.ValidateWrites"/> against a live router: a renamed field written under the name the
    /// router takes, a field it takes under no name refused before anything is sent — a list included.
    /// </summary>
    [TestClass]
    public class ValidateWritesTest : TestBase
    {
        /// <summary><c>/ip/route</c> with one field no RouterOS has.</summary>
        [TikEntity("/ip/route")]
        public class BogusRoute
        {
            [TikProperty(".id", IsReadOnly = true, IsMandatory = true)]
            public string Id { get; private set; }

            [TikProperty("dst-address")]
            public string DstAddress { get; set; }

            [TikProperty("gateway")]
            public string Gateway { get; set; }

            [TikProperty("disabled")]
            public bool? Disabled { get; set; }

            [TikProperty("comment")]
            public string Comment { get; set; }

            [TikProperty("t4n-bogus")]
            public string Bogus { get; set; }
        }

        private ITikConnection OpenValidating()
        {
            var type = ResolveConnectionType();
            if (type == TikConnectionType.WinboxNative || type == TikConnectionType.WinboxNativeMac)
                Assert.Inconclusive("WinBox native refuses a field it has no key for by itself; ValidateWrites skips it.");
            bool isCli = type == TikConnectionType.Telnet || type == TikConnectionType.Ssh || type == TikConnectionType.MacTelnet
                || type == TikConnectionType.WinboxCli || type == TikConnectionType.WinboxCliMac;
            if (GetMikrotikVersion().Major < 7 && !isCli)
                Assert.Inconclusive("RouterOS 6 over the API cannot describe a menu; the write goes out unchecked.");

            var setup = LabSetup(type);
            setup.ValidateWrites = true;
            return setup.Create(type);
        }

        private List<IpRoute> RoutesCommented(string comment)
            => Connection.LoadAll<IpRoute>().Where(r => r.Comment.Value == comment).ToList();

        [TestMethod]
        public void ANewRouteGetsItsRoutingTableUnderTheNameThisRouterTakes()
        {
            // Without the check a new route is added as routing-table, which RouterOS 6 refuses.
            string tag = Guid.NewGuid().ToString("N").Substring(0, 8);
            string table = "t4n-vw-" + tag;
            using (var validating = OpenValidating())
            {
                if (GetMikrotikVersion().Major >= 7)
                    SaveTracked(new RoutingTable { Name = table, Fib = true, Comment = "t4n-vw-" + tag });

                var route = new IpRoute
                {
                    DstAddress = "203.0.113.78/32",
                    Gateway = "127.0.0.1",
                    Disabled = true,
                    Comment = "t4n-vw-" + tag,
                    RoutingTable = table,
                };
                try { validating.Save(route); }
                finally { foreach (var r in RoutesCommented("t4n-vw-" + tag)) TrackForCleanup(r); }

                Assert.AreEqual(table, Connection.LoadById<IpRoute>(route.Id).RoutingTable.Value);
            }
        }

        [TestMethod]
        public void AFieldTheRouterDoesNotTake_IsRefusedAndNothingIsSent()
        {
            string comment = "t4n-vw-" + Guid.NewGuid().ToString("N").Substring(0, 8);
            using (var validating = OpenValidating())
            {
                var route = new BogusRoute { DstAddress = "203.0.113.79/32", Gateway = "127.0.0.1", Disabled = true, Comment = comment, Bogus = "1" };

                var ex = Assert.ThrowsException<TikUnknownFieldException>(() => validating.Save(route));

                CollectionAssert.AreEqual(new[] { "t4n-bogus" }, ex.Fields.ToArray());
                var created = RoutesCommented(comment);
                created.ForEach(r => TrackForCleanup(r));
                Assert.AreEqual(0, created.Count, "the route was created although the write was refused");
            }
        }

        [TestMethod]
        public void AListIsCheckedWhole_BeforeItsFirstRowIsWritten()
        {
            string comment = "t4n-vw-" + Guid.NewGuid().ToString("N").Substring(0, 8);
            using (var validating = OpenValidating())
            {
                var rows = new List<BogusRoute>
                {
                    new BogusRoute { DstAddress = "203.0.113.80/32", Gateway = "127.0.0.1", Disabled = true, Comment = comment },
                    new BogusRoute { DstAddress = "203.0.113.81/32", Gateway = "127.0.0.1", Disabled = true, Comment = comment, Bogus = "1" },
                };

                Assert.ThrowsException<TikUnknownFieldException>(
                    () => validating.SaveListDifferences(rows, Enumerable.Empty<BogusRoute>()));

                var created = RoutesCommented(comment);
                created.ForEach(r => TrackForCleanup(r));
                Assert.AreEqual(0, created.Count, "the first row was written before the second was refused");
            }
        }
    }
}
