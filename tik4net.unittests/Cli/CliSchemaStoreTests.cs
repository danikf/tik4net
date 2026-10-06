// CliSchemaStoreTests.cs — the persistent CLI menu grammar: what is stored, how two writers merge, and that no
// cache problem ever reaches the caller (the design rule: a cache may only cost the probes it was meant to save).

#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using tik4net.Cli;
using tik4net.Connection;

namespace tik4net.unittests.Cli
{
    [TestClass]
    [DoNotParallelize]   // CliSchemaStore.SaveDelay and the process-wide registry are static
    public class CliSchemaStoreTests
    {
        private const string Key = "7.24.4|x86_64|container,routeros";
        private const string File = @"C:\cache\cli-schema\v2\7.24.4\abcd1234.json";

        /// <summary>An in-memory file system that can be told to fail at each step.</summary>
        private sealed class FakeFs : ITikCacheFileSystem
        {
            public readonly Dictionary<string, string> Files = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            public bool FailRead, FailWrite, FailLock, LockBusy;
            public int Writes;

            public string? ReadAllText(string path)
            {
                if (FailRead) throw new IOException("read refused");
                lock (Files) return Files.TryGetValue(path, out var t) ? t : null;
            }

            public void WriteAtomically(string path, string text)
            {
                if (FailWrite) throw new IOException("disk full");
                lock (Files) { Files[path] = text; Writes++; }
            }

            public IDisposable? TryLock(string path, TimeSpan timeout)
            {
                if (FailLock) throw new UnauthorizedAccessException("no access");
                return LockBusy ? null : new Released();
            }

            private sealed class Released : IDisposable { public void Dispose() { } }
        }

        private TimeSpan _savedDelay;

        [TestInitialize]
        public void Init()
        {
            _savedDelay = CliSchemaStore.SaveDelay;
            CliSchemaStore.SaveDelay = TimeSpan.FromHours(1);   // saves are explicit unless a test says otherwise
            CliSchemaStore.ResetRegistryForTests();
        }

        [TestCleanup]
        public void Cleanup()
        {
            CliSchemaStore.SaveDelay = _savedDelay;
            CliSchemaStore.ResetRegistryForTests();
        }

        private static CliSchemaStore Store(FakeFs fs, string key = Key) => new CliSchemaStore(File, key, fs);

        private static string[]? Get(CliSchemaStore store, string menu, string list)
            => store.TryGet(menu, list, out var value) ? value?.ToArray() : new[] { "<not stored>" };

        // ── What is stored ─────────────────────────────────────────────────────

        [TestMethod]
        public void ListsSurviveASaveAndALoad_IncludingAListTheRouterDoesNotHave()
        {
            var fs = new FakeFs();
            var a = Store(fs);
            a.Put("/ip/route", TikMenuSchemaSource.ConsoleInspect, "readable", new[] { "dst-address", "routing-table" });
            a.Put("/ip/route", TikMenuSchemaSource.ConsoleInspect, "unset", null);
            a.Flush();

            var b = Store(fs);
            CollectionAssert.AreEqual(new[] { "dst-address", "routing-table" }, Get(b, "/ip/route", "readable"));
            Assert.IsTrue(b.TryGet("/ip/route", "unset", out var unset) && unset == null, "asked, and the router has none");
            Assert.IsFalse(b.TryGet("/ip/route", "args:add", out _), "never asked is not stored");
            Assert.AreEqual(TikMenuSchemaSource.ConsoleInspect, b.SourceOf("/ip/route"));
        }

        [TestMethod]
        public void NothingNewMeansNoWrite()
        {
            var fs = new FakeFs();
            var a = Store(fs);
            a.Put("/ip/route", TikMenuSchemaSource.ConsoleInspect, "readable", new[] { "x" });
            a.Flush();
            a.Put("/ip/route", TikMenuSchemaSource.ConsoleInspect, "readable", new[] { "x" });   // the same answer again
            a.Flush();
            Assert.AreEqual(1, fs.Writes);
        }

        [TestMethod]
        public void TheKeyDropsTheChannelAndSortsThePackages()
        {
            Assert.AreEqual("7.24.4|x86_64|container,routeros",
                CliSchemaStore.KeyFromAnswer("\r#t4n-build=7.24.4 (stable)|x86_64|routeros,container,\n"));
            Assert.AreEqual("6.49.13|x86_64|dhcp,routeros-x86",
                CliSchemaStore.KeyFromAnswer("#t4n-build=6.49.13 (long-term)|x86_64|routeros-x86,dhcp,"));
            Assert.IsNull(CliSchemaStore.KeyFromAnswer("bad command name package (line 1 column 30)"));
            Assert.IsNull(CliSchemaStore.KeyFromAnswer(CliSchemaStore.KeyCommand), "the echoed command is not the answer");
        }

        [TestMethod]
        public void TwoBuildsGetTwoFiles_InTheFormatsOwnDirectory()
        {
            string a = CliSchemaStore.PathFor(@"C:\c", "7.24.4|x86_64|routeros");
            string b = CliSchemaStore.PathFor(@"C:\c", "7.24.4|x86_64|routeros,wifi-qcom");
            Assert.AreNotEqual(a, b);
            StringAssert.StartsWith(a, Path.Combine(@"C:\c", "cli-schema", "v" + CliSchemaStore.FormatVersion, "7.24.4"));
        }

        [TestMethod]
        public void NoDirectoryMeansNoStore()
        {
            Assert.IsNull(CliSchemaStore.For(null, Key));
            Assert.IsNull(CliSchemaStore.For("", Key));
            Assert.IsNull(CliSchemaStore.For(@"C:\c", null));
        }

        // ── Two writers ────────────────────────────────────────────────────────

        [TestMethod]
        public void TwoProcessesThatLearnDifferentMenus_BothKeepTheirs()
        {
            var fs = new FakeFs();
            var one = Store(fs);
            var two = Store(fs);
            one.Put("/ip/route", TikMenuSchemaSource.ConsoleInspect, "readable", new[] { "dst-address" });
            two.Put("/interface", TikMenuSchemaSource.ConsoleInspect, "readable", new[] { "name" });
            one.Flush();
            two.Flush();   // merges what one wrote before it writes

            var reader = Store(fs);
            CollectionAssert.AreEqual(new[] { "dst-address" }, Get(reader, "/ip/route", "readable"));
            CollectionAssert.AreEqual(new[] { "name" }, Get(reader, "/interface", "readable"));
        }

        [TestMethod]
        public void FeaturesAndFactsSurviveASaveAndALoad()
        {
            var fs = new FakeFs();
            var a = Store(fs);
            a.PutFeature("dsv:~^~", false);
            a.PutFact("flagFieldExists", "/ip address|disabled");
            a.Flush();

            var b = Store(fs);
            Assert.IsTrue(b.TryGetFeature("dsv:~^~", out bool dsv) && !dsv);
            Assert.IsFalse(b.TryGetFeature("consoleInspect", out _), "never learnt is not stored");
            CollectionAssert.AreEqual(new[] { "/ip address|disabled" }, b.Facts("flagFieldExists").ToArray());
            Assert.AreEqual(0, b.Facts("printWithoutAsValue").Count);
        }

        [TestMethod]
        public void TwoProcessesThatLearnDifferentFacts_BothKeepTheirs()
        {
            var fs = new FakeFs();
            var one = Store(fs);
            var two = Store(fs);
            one.PutFact("flagFieldExists", "/ip address|disabled");
            one.PutFeature("printProplist", false);
            two.PutFact("flagFieldExists", "/ip firewall filter|invalid");
            two.PutFeature("consoleInspect", false);
            one.Flush();
            two.Flush();

            var reader = Store(fs);
            CollectionAssert.AreEquivalent(new[] { "/ip address|disabled", "/ip firewall filter|invalid" },
                reader.Facts("flagFieldExists").ToArray());
            Assert.IsTrue(reader.TryGetFeature("printProplist", out _));
            Assert.IsTrue(reader.TryGetFeature("consoleInspect", out _));
        }

        [TestMethod]
        public void ASaveThatFindsTheLockHeld_IsDeferredNotLost()
        {
            var fs = new FakeFs { LockBusy = true };
            var a = Store(fs);
            a.Put("/ip/route", TikMenuSchemaSource.ConsoleInspect, "readable", new[] { "x" });
            a.Flush();
            Assert.AreEqual(0, fs.Writes);

            fs.LockBusy = false;
            a.Flush();
            Assert.AreEqual(1, fs.Writes);
        }

        [TestMethod]
        public void ABurstOfNewListsIsOneWrite()
        {
            CliSchemaStore.SaveDelay = TimeSpan.FromMilliseconds(100);
            var fs = new FakeFs();
            var a = Store(fs);
            for (int i = 0; i < 20; i++)
                a.Put("/m" + i, TikMenuSchemaSource.ConsoleInspect, "readable", new[] { "f" });
            Assert.AreEqual(0, fs.Writes, "nothing is written per list");

            var deadline = DateTime.UtcNow.AddSeconds(5);
            while (fs.Writes == 0 && DateTime.UtcNow < deadline)
                Thread.Sleep(20);
            Thread.Sleep(300);
            Assert.AreEqual(1, fs.Writes);
        }

        // ── A cache problem never throws ──────────────────────────────────────

        [TestMethod]
        public void AnUnreadableFile_ReadsAsEmpty()
        {
            var fs = new FakeFs { FailRead = true };
            var a = Store(fs);
            Assert.IsFalse(a.TryGet("/ip/route", "readable", out _));
            a.Put("/ip/route", TikMenuSchemaSource.ConsoleInspect, "readable", new[] { "x" });
            CollectionAssert.AreEqual(new[] { "x" }, Get(a, "/ip/route", "readable"));
        }

        [TestMethod]
        public void ACorruptFile_ReadsAsEmpty_AndIsReplacedByTheNextSave()
        {
            var fs = new FakeFs();
            fs.Files[File] = "{ not json";
            var a = Store(fs);
            Assert.IsFalse(a.TryGet("/ip/route", "readable", out _));
            a.Put("/ip/route", TikMenuSchemaSource.ConsoleInspect, "readable", new[] { "x" });
            a.Flush();
            CollectionAssert.AreEqual(new[] { "x" }, Get(Store(fs), "/ip/route", "readable"));
        }

        [TestMethod]
        public void AnotherFormatOrBuild_IsNotRead()
        {
            var fs = new FakeFs();
            fs.Files[File] = "{\"format\":99,\"key\":\"" + Key + "\",\"menus\":{\"/ip/route\":{\"source\":\"ConsoleInspect\",\"lists\":{\"readable\":[\"x\"]}}}}";
            Assert.IsFalse(Store(fs).TryGet("/ip/route", "readable", out _));
            fs.Files[File] = "{\"format\":" + CliSchemaStore.FormatVersion + ",\"key\":\"6.49.13|x86_64|routeros-x86\",\"menus\":{\"/ip/route\":{\"source\":\"ConsoleInspect\",\"lists\":{\"readable\":[\"x\"]}}}}";
            Assert.IsFalse(Store(fs).TryGet("/ip/route", "readable", out _));
        }

        [TestMethod]
        public void AMalformedEntry_IsDropped_AndTheRestIsUsed()
        {
            var fs = new FakeFs();
            fs.Files[File] = "{\"format\":" + CliSchemaStore.FormatVersion + ",\"key\":\"" + Key + "\",\"menus\":{"
                + "\"/bad\":{\"source\":\"ConsoleInspect\",\"lists\":{\"readable\":[1,2]}},"
                + "\"/odd\":{\"source\":\"NoSuchSource\",\"lists\":{\"readable\":[\"x\"]}},"
                + "\"/ip/route\":{\"source\":\"ConsoleInspect\",\"lists\":{\"readable\":[\"dst-address\"]}}}}";
            var a = Store(fs);
            Assert.IsFalse(a.TryGet("/bad", "readable", out _));
            Assert.IsFalse(a.TryGet("/odd", "readable", out _));
            CollectionAssert.AreEqual(new[] { "dst-address" }, Get(a, "/ip/route", "readable"));
        }

        [TestMethod]
        public void AFailedWriteOrLock_DoesNotThrow_AndIsRetried()
        {
            var fs = new FakeFs { FailWrite = true };
            var a = Store(fs);
            a.Put("/ip/route", TikMenuSchemaSource.ConsoleInspect, "readable", new[] { "x" });
            a.Flush();
            fs.FailWrite = false;
            fs.FailLock = true;
            a.Flush();
            fs.FailLock = false;
            a.Flush();
            CollectionAssert.AreEqual(new[] { "x" }, Get(Store(fs), "/ip/route", "readable"));
        }

        [TestMethod]
        public void ATimerSaveThatFails_DoesNotEndTheProcess()
        {
            // An exception on a timer thread would end the process: the save is caught inside the callback. The test
            // runs to its end (and the run with it) only if nothing escaped.
            CliSchemaStore.SaveDelay = TimeSpan.FromMilliseconds(50);
            var fs = new FakeFs { FailWrite = true };
            Exception? escaped = null;
            UnhandledExceptionEventHandler handler = (s, e) => escaped = e.ExceptionObject as Exception;
            AppDomain.CurrentDomain.UnhandledException += handler;
            try
            {
                var a = Store(fs);
                a.Put("/ip/route", TikMenuSchemaSource.ConsoleInspect, "readable", new[] { "x" });
                Thread.Sleep(400);
            }
            finally
            {
                AppDomain.CurrentDomain.UnhandledException -= handler;
            }
            Assert.IsNull(escaped);
        }

        // ── Through a CLI connection ───────────────────────────────────────────

        private const string RouteNode =
            "name=route;node-type=dir;type=self;name=get;node-type=cmd;type=child;name=print;node-type=cmd;type=child";

        /// <summary>A 7.24 router for /ip/route: answers the build question, /console/inspect, and every print.</summary>
        private sealed class BuildRouter : CliConnectionBase
        {
            public readonly List<string> Sent = new List<string>();
            public string Readable = "dst-address,routing-table";
            public string Build = "7.24.4 (stable)|x86_64|routeros,";

            public BuildRouter(string? cacheDir)
            {
                CliFieldSeparator = null;
                CatalogCachePath = cacheDir;
            }

            protected override string TransportName => "Build";

            public void OpenScripted()
                => OpenWith(_ => Task.FromResult(0), SendAsync, (raw, ct) => Task.FromResult(string.Empty), () => { });

            private Task<string> SendAsync(string cliText, CancellationToken ct)
            {
                Sent.Add(cliText);
                if (cliText.Contains(CliSchemaStore.KeyMarker))
                    return Task.FromResult(CliSchemaStore.KeyMarker + Build);
                if (cliText.Contains("/console inspect"))
                {
                    if (cliText.Contains("request=child"))
                        return Task.FromResult(RouteNode);
                    return Task.FromResult(string.Join(";", Readable.Split(',')
                        .Select(f => "completion=" + f + ";show=true;type=completion")));
                }
                return Task.FromResult(CountedReadFake.Answer(cliText,
                    ".id=*1;dst-address=0.0.0.0/0;routing-table=main;.id=*2;dst-address=192.168.4.0/24;routing-table=main"));
            }

            public int Inspects => Sent.Count(s => s.Contains("/console inspect"));

            public override void Open(string host, string user, string password) => OpenScripted();
            public override void Open(string host, int port, string user, string password) => OpenScripted();
            public override Task OpenAsync(string host, string user, string password, CancellationToken cancellationToken = default) { OpenScripted(); return Task.FromResult(0); }
            public override Task OpenAsync(string host, int port, string user, string password, CancellationToken cancellationToken = default) { OpenScripted(); return Task.FromResult(0); }
        }

        /// <summary>
        /// A RouterOS 6 build for /ip/route: refuses the DSV probe ("bad command name serialize") and reads as-value.
        /// </summary>
        private sealed class Dsv6Router : CliConnectionBase
        {
            public readonly List<string> Sent = new List<string>();

            public Dsv6Router(string? cacheDir)
            {
                CliFieldSeparator = "~^~";
                CatalogCachePath = cacheDir;
            }

            protected override string TransportName => "Dsv6";

            public void OpenScripted()
                => OpenWith(_ => Task.FromResult(0), SendAsync, (raw, ct) => Task.FromResult(string.Empty), () => { });

            private Task<string> SendAsync(string cliText, CancellationToken ct)
            {
                Sent.Add(cliText);
                if (cliText.Contains(CliSchemaStore.KeyMarker))
                    return Task.FromResult(CliSchemaStore.KeyMarker + "6.49.13 (long-term)|x86_64|system,routing,");
                if (cliText.Contains(":serialize to=dsv"))
                    return Task.FromResult("bad command name serialize (line 1 column 16)");
                return Task.FromResult(CountedReadFake.Answer(cliText, ".id=*1;dst-address=0.0.0.0/0;routing-table=main"));
            }

            public int DsvProbes => Sent.Count(s => s.Contains(":serialize to=dsv"));

            public override void Open(string host, string user, string password) => OpenScripted();
            public override void Open(string host, int port, string user, string password) => OpenScripted();
            public override Task OpenAsync(string host, string user, string password, CancellationToken cancellationToken = default) { OpenScripted(); return Task.FromResult(0); }
            public override Task OpenAsync(string host, int port, string user, string password, CancellationToken cancellationToken = default) { OpenScripted(); return Task.FromResult(0); }
        }

        [TestMethod]
        public void AFeatureLearntByOneConnection_IsNotAskedByTheNextToTheSameBuild()
        {
            string dir = NewCacheDir();
            try
            {
                using (var cold = new Dsv6Router(dir))
                {
                    cold.OpenScripted();
                    cold.CreateCommand("/ip/route/print").ExecuteList();
                    Assert.AreEqual(1, cold.DsvProbes);
                }

                CliSchemaStore.ResetRegistryForTests();   // as a new process: the file is all it has
                using (var warm = new Dsv6Router(dir))
                {
                    warm.OpenScripted();
                    Assert.AreEqual(1, warm.CreateCommand("/ip/route/print").ExecuteList().Count());
                    Assert.AreEqual(0, warm.DsvProbes, string.Join(" | ", warm.Sent));
                }
            }
            finally { Delete(dir); }
        }

        [TestMethod]
        public void WithTheCacheOff_EveryConnectionAsksTheFeature()
        {
            for (int i = 0; i < 2; i++)
                using (var router = new Dsv6Router(null))
                {
                    router.OpenScripted();
                    router.CreateCommand("/ip/route/print").ExecuteList();
                    Assert.AreEqual(1, router.DsvProbes);
                    Assert.IsFalse(router.Sent.Any(t => t.Contains(CliSchemaStore.KeyMarker)));
                }
        }

        private static string NewCacheDir() => Path.Combine(Path.GetTempPath(), "t4n-unit-cache-" + Guid.NewGuid().ToString("N"));

        private static void Delete(string dir)
        {
            try { Directory.Delete(dir, recursive: true); } catch { }
        }

        private static List<ITikReSentence> Read(ITikConnection c, string name, string value)
            => c.CreateCommand("/ip/route/print", c.CreateParameter(name, value, TikCommandParameterFormat.Filter))
                .ExecuteList().ToList();

        [TestMethod]
        public void ASecondConnectionToTheSameBuild_AsksTheRouterNothing()
        {
            string dir = NewCacheDir();
            try
            {
                using (var cold = new BuildRouter(dir))
                {
                    cold.OpenScripted();
                    Read(cold, "routing-table", "main");
                    Assert.IsTrue(cold.Inspects > 0);
                }   // Close saves

                CliSchemaStore.ResetRegistryForTests();   // as a new process: the file is all it has
                using (var warm = new BuildRouter(dir))
                {
                    warm.OpenScripted();
                    Assert.AreEqual(2, Read(warm, "routing-table", "main").Count);
                    Assert.AreEqual(0, warm.Inspects, string.Join(" | ", warm.Sent));
                    Assert.AreEqual(1, warm.Sent.Count(s => s.Contains(CliSchemaStore.KeyMarker)), "the build, once");
                }
            }
            finally { Delete(dir); }
        }

        [TestMethod]
        public void AnotherBuild_DoesNotUseTheCache()
        {
            string dir = NewCacheDir();
            try
            {
                using (var a = new BuildRouter(dir)) { a.OpenScripted(); Read(a, "routing-table", "main"); }
                using (var b = new BuildRouter(dir) { Build = "7.24.5 (stable)|x86_64|routeros," })
                {
                    b.OpenScripted();
                    Read(b, "routing-table", "main");
                    Assert.IsTrue(b.Inspects > 0);
                }
            }
            finally { Delete(dir); }
        }

        [TestMethod]
        public void ACachedListThatLacksAField_IsAskedAgain_BeforeAFilterIsRefused()
        {
            string dir = NewCacheDir();
            try
            {
                using (var old = new BuildRouter(dir) { Readable = "dst-address" })
                {
                    old.OpenScripted();
                    Read(old, "dst-address", "0.0.0.0/0");
                }

                using (var now = new BuildRouter(dir))   // this router reads routing-table too
                {
                    now.OpenScripted();
                    Assert.AreEqual(2, Read(now, "routing-table", "main").Count, "a stale cache must not refuse");
                    Assert.IsTrue(now.Inspects > 0);
                    int asked = now.Inspects;
                    Read(now, "routing-table", "main");
                    Assert.AreEqual(asked, now.Inspects, "the corrected list is remembered");
                }

                using (var later = new BuildRouter(dir))
                {
                    later.OpenScripted();
                    Read(later, "routing-table", "main");
                    Assert.AreEqual(0, later.Inspects, "and saved");
                }
            }
            finally { Delete(dir); }
        }

        [TestMethod]
        public void AFieldTheRouterReallyLacks_IsStillRefused_FromACachedList()
        {
            string dir = NewCacheDir();
            try
            {
                using (var a = new BuildRouter(dir)) { a.OpenScripted(); Read(a, "dst-address", "0.0.0.0/0"); }
                using (var b = new BuildRouter(dir))
                {
                    b.OpenScripted();
                    Assert.ThrowsException<TikUnknownFieldException>(() => Read(b, "routing-mark", "main"));
                }
            }
            finally { Delete(dir); }
        }

        [TestMethod]
        public void WithTheCacheOff_TheBuildIsNotAsked_AndNothingIsWritten()
        {
            using (var router = new BuildRouter(null))
            {
                router.OpenScripted();
                Read(router, "routing-table", "main");
                Assert.IsFalse(router.Sent.Any(s => s.Contains(CliSchemaStore.KeyMarker)));
            }
        }

        [TestMethod]
        public void ARouterThatCannotSayItsBuild_WorksWithoutTheCache()
        {
            string dir = NewCacheDir();
            try
            {
                using (var router = new BuildRouter(dir) { Build = "\nbad command name package" })
                {
                    router.OpenScripted();
                    Assert.AreEqual(2, Read(router, "routing-table", "main").Count);
                }
                Assert.IsFalse(Directory.Exists(dir), "nothing written");
            }
            finally { Delete(dir); }
        }

        [TestMethod]
        public void AValueCompletion_IsNeverStored()
        {
            string dir = NewCacheDir();
            try
            {
                using (var a = new BuildRouter(dir))
                {
                    a.OpenScripted();
                    var schema = a.DescribeMenu("/ip/route");
                    Assert.IsNotNull(schema.ReadableFields);   // a list, so there is a file
                    schema.ValuesOf("routing-table");
                }
                string json = Directory.GetFiles(dir, "*.json", SearchOption.AllDirectories)
                    .Select(System.IO.File.ReadAllText).Single();
                using (var doc = System.Text.Json.JsonDocument.Parse(json))
                {
                    var lists = doc.RootElement.GetProperty("menus").GetProperty("/ip/route").GetProperty("lists")
                        .EnumerateObject().Select(l => l.Name).ToList();
                    CollectionAssert.IsSubsetOf(lists, new[] { "commands", "submenus", "readable", "unset" },
                        "only grammar lists: " + string.Join(", ", lists));
                }
            }
            finally { Delete(dir); }
        }
    }
}
