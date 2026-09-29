// FilterValidationTests.cs — a filter on a field the menu does not have is refused before the read is sent.
//
// Without the check the transports answer it wrongly in two different ways: the API with no rows, the CLI with every
// row — RouterOS 7.24.4 evaluates 'where routing-mark=main' (the 6.x name) as true for each one (findings-cli §2).
// The inspect answers below are 7.24.4's for /ip/route, shortened.

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
    [TestClass]
    public class FilterValidationTests
    {
        private const string RouteNode =
            "name=route;node-type=dir;type=self;name=get;node-type=cmd;type=child;name=print;node-type=cmd;type=child";
        private const string RouteReadable =
            "completion=dst-address;show=true;type=completion;completion=routing-table;show=true;type=completion;"
            + "completion=*;show=false;type=completion";

        /// <summary>Answers /console/inspect for /ip/route (or refuses it, as 6.49.13) and every print with two rows.</summary>
        private sealed class InspectRouter : CliConnectionBase
        {
            private readonly bool _hasInspect, _menuHasGet;
            public readonly List<string> Sent = new List<string>();

            public InspectRouter(bool hasInspect = true, bool menuHasGet = true)
            {
                CliFieldSeparator = null;   // as-value; the DSV read is CliDsvReadTests
                _hasInspect = hasInspect;
                _menuHasGet = menuHasGet;
            }

            protected override string TransportName => "Inspect";

            public void OpenScripted()
                => OpenWith(_ => Task.FromResult(0), SendAsync, (raw, ct) => Task.FromResult(string.Empty), () => { });

            private Task<string> SendAsync(string cliText, CancellationToken ct)
            {
                Sent.Add(cliText);
                if (cliText.Contains("/console inspect"))
                {
                    if (!_hasInspect)
                        return Task.FromResult("bad command name inspect (line 1 column 15)");
                    if (cliText.Contains("request=child"))
                        return Task.FromResult(_menuHasGet ? RouteNode : RouteNode.Replace("name=get;node-type=cmd;type=child;", ""));
                    return Task.FromResult(RouteReadable);
                }
                return Task.FromResult(CountedReadFake.Answer(cliText,
                    ".id=*1;dst-address=0.0.0.0/0;routing-table=main;.id=*2;dst-address=192.168.4.0/24;routing-table=main"));
            }

            public override void Open(string host, string user, string password) => OpenScripted();
            public override void Open(string host, int port, string user, string password) => OpenScripted();
            public override Task OpenAsync(string host, string user, string password, CancellationToken cancellationToken = default) { OpenScripted(); return Task.FromResult(0); }
            public override Task OpenAsync(string host, int port, string user, string password, CancellationToken cancellationToken = default) { OpenScripted(); return Task.FromResult(0); }
        }

        private static InspectRouter Open(bool hasInspect = true, bool menuHasGet = true)
        {
            var router = new InspectRouter(hasInspect, menuHasGet);
            router.OpenScripted();
            return router;
        }

        // A read at the command level, where the check sits — every entity load comes through it too.
        private static List<ITikReSentence> Read(ITikConnection c, string name, string value)
            => c.CreateCommand("/ip/route/print", c.CreateParameter(name, value, TikCommandParameterFormat.Filter))
                .ExecuteList().ToList();

        [TestMethod]
        public void AFilterOnAFieldTheMenuDoesNotHave_IsRefused_AndThePrintIsNotSent()
        {
            using (var router = Open())
            {
                var ex = Assert.ThrowsException<TikUnknownFieldException>(
                    () => Read(router, "routing-mark", "main"));

                CollectionAssert.AreEqual(new[] { "routing-mark" }, ex.Fields.ToArray());
                Assert.AreEqual(TikUnknownFieldUse.Filter, ex.Use);
                Assert.IsFalse(router.Sent.Any(s => s.Contains(" print ")), string.Join(" | ", router.Sent));
            }
        }

        [TestMethod]
        public void AFilterOnAFieldTheMenuHas_IsSent()
        {
            using (var router = Open())
            {
                Assert.AreEqual(2, Read(router, "routing-table", "main").Count);
                Assert.IsTrue(router.Sent.Any(s => s.Contains(" print ")));
            }
        }

        [TestMethod]
        public void TheMenuIsAskedOnce_AndAnIdFilterNeverAsks()
        {
            using (var router = Open())
            {
                Read(router, ".id", "*1");
                Assert.IsFalse(router.Sent.Any(s => s.Contains("/console inspect")), "an .id filter is not a field of the menu");

                Read(router, "routing-table", "main");
                Read(router, "dst-address", "0.0.0.0/0");
                Assert.AreEqual(2, router.Sent.Count(s => s.Contains("/console inspect")), "the menu node and its readable fields, once");
            }
        }

        [TestMethod]
        public void ARouterThatCannotSay_IsAskedOnce_NotBeforeEveryRead()
        {
            using (var router = Open(hasInspect: false))
            {
                Read(router, "t4n-bogus", "1");
                Read(router, "t4n-bogus", "1");

                Assert.AreEqual(1, router.Sent.Count(s => s.Contains("/console inspect")), string.Join(" | ", router.Sent));
            }
        }

        [TestMethod]
        public void WhereTheRouterCannotSay_TheReadIsSentAsBefore()
        {
            // 6.49.13 has no /console/inspect; this double has no Tab either. A menu without 'get' cannot say either.
            using (var router = Open(hasInspect: false))
                Assert.AreEqual(2, Read(router, "t4n-bogus", "1").Count);
            using (var router = Open(menuHasGet: false))
                Assert.AreEqual(2, Read(router, "t4n-bogus", "1").Count);
        }
    }
}
