// TransportHintsTests.cs — what the O/R mapper tells tik4net's own transports about an entity (TikTransportHints)
// travels beside the command's parameters: never as a word, set by every mapper command it belongs to, absent from a
// raw command, and kept by every descriptor a transport derives from another one. A dropped hint fails silently —
// counters, secrets or flags read absent and nothing throws — so each path has its own test.

#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using tik4net.Connection;
using tik4net.Objects;
using tik4net.Testing;
using tik4net.unittests.Docs;

namespace tik4net.unittests.Connection
{
    [TestClass]
    public class TransportHintsTests
    {
        // Every option that feeds a hint, on one entity.
        [TikEntity("/hinted", IncludeCliStats = true)]
        public class HintedRow
        {
            [TikProperty(".id", IsReadOnly = true, IsMandatory = true)]
            public string? Id { get; private set; }

            [TikProperty("name", WinboxLabel = "Upstream Sync")]
            public TikField<string?> Name { get; set; }

            [TikProperty("script", IsFreeText = true)]
            public TikField<string?> Script { get; set; }

            [TikProperty("secret", IsSensitive = true)]
            public TikField<string?> Secret { get; set; }

            [TikProperty("disabled")]
            public TikField<bool?> Disabled { get; set; }

            [TikProperty("running", IsReadOnly = true)]
            public TikField<bool?> Running { get; set; }
        }

        [TikEntity("/hinted/single", IsSingleton = true)]
        public class HintedSingleton
        {
            [TikProperty("disabled")]
            public TikField<bool?> Disabled { get; set; }

            [TikProperty("running", IsReadOnly = true)]
            public TikField<bool?> Running { get; set; }
        }

        [TikEntity("/plain")]
        public class PlainRow
        {
            [TikProperty(".id", IsReadOnly = true, IsMandatory = true)]
            public string? Id { get; private set; }

            [TikProperty("name")]
            public TikField<string?> Name { get; set; }
        }

        private static TikTransportHints Read<T>() => TikEntityMetadataCache.GetMetadata<T>().ReadHints;
        private static TikTransportHints Labels<T>() => TikEntityMetadataCache.GetMetadata<T>().LabelHints;

        private static bool IsMarkerWord(string word)
            => word.Contains(".cli-") || word.Contains(".winbox-") || word.Contains("Upstream");

        // ── No hint is a word ─────────────────────────────────────────────────

        [TestMethod]
        public void NoMapperReadOrWrite_PutsAHintIntoTheParameters()
        {
            var connection = new TikFakeConnection { Capabilities = new TikFakeConnection().Capabilities | TikConnectionCapability.FieldLabels }
                .WithResponse(cmd => cmd.First() == "/hinted/print", _ => new ITikSentence[]
                {
                    new TikFakeReSentence(new Dictionary<string, string> { [".id"] = "*1", ["name"] = "a", ["script"] = "x" }),
                    new TikFakeDoneSentence(),
                })
                .WithScalarResponse(cmd => cmd.First() == "/hinted/add", "*2")
                .WithNonQuery(cmd => cmd.First() == "/hinted/set" || cmd.First() == "/hinted/unset");

            var row = connection.LoadAll<HintedRow>().Single();
            row.Name = "b";
            row.Script = null;                       // an unset
            connection.Save(row);
            connection.Save(new HintedRow { Name = "c" });

            var verbs = connection.SentCommands.Select(c => c.First()).ToList();
            CollectionAssert.IsSubsetOf(new[] { "/hinted/print", "/hinted/set", "/hinted/unset", "/hinted/add" }, verbs,
                string.Join(", ", verbs));
            var words = connection.SentCommands.SelectMany(c => c.Skip(1)).Where(IsMarkerWord).ToList();
            Assert.AreEqual(0, words.Count, "hints sent as words: " + string.Join(", ", words));

            var listen = TikConnectionExtensions.CreateListenCommandWithFilter<HintedRow>(connection);
            Assert.AreEqual(0, listen.Parameters.Count, "the listen command carries no word for its labels");
        }

        // ── Each option produces its hint, on the commands it belongs to ──────

        [TestMethod]
        public void TheLoad_CarriesEveryReadHint()
        {
            var hints = TikConnectionExtensions.CreateLoadCommandWithFilter<HintedRow>(new TikFakeConnection()).HintsOf();

            Assert.IsTrue(hints.MergeStats, "IncludeCliStats");
            Assert.IsTrue(hints.SerializeRead, "IsFreeText");
            CollectionAssert.AreEqual(new[] { "script" }, hints.FreeTextFields.ToArray(), "IsFreeText");
            Assert.IsTrue(hints.ShowSensitive, "IsSensitive");
            CollectionAssert.AreEquivalent(new[] { "disabled", "running" }, hints.FlagFields.ToArray(), "disabled and the read-only bool");
            Assert.IsTrue(hints.WinboxLabels!.ContainsKey("name"), "WinboxLabel");
        }

        [TestMethod]
        public void AnEntityWithoutTheOptions_CarriesNoReadHint()
        {
            var hints = TikConnectionExtensions.CreateLoadCommandWithFilter<PlainRow>(new TikFakeConnection()).HintsOf();

            Assert.IsFalse(hints.MergeStats || hints.SerializeRead || hints.ShowSensitive);
            Assert.AreEqual(0, hints.FlagFields.Count + hints.FreeTextFields.Count);
            Assert.IsNull(hints.WinboxLabels);
            Assert.IsNull(hints.WinboxLabelsKey);
        }

        [TestMethod]
        public void ASingleton_IsNotAskedForItsFlagsByName()
            // Its plain print carries its flags on every version, and it takes no proplist= at all.
            => Assert.AreEqual(0, TikConnectionExtensions.CreateLoadCommandWithFilter<HintedSingleton>(new TikFakeConnection())
                .HintsOf().FlagFields.Count);

        [TestMethod]
        public void TheWrites_AndTheListen_CarryTheLabelsAndNothingElse()
        {
            var connection = new TikFakeConnection()
                .WithResponse(cmd => cmd.First() == "/hinted/print", _ => new ITikSentence[]
                {
                    new TikFakeReSentence(new Dictionary<string, string> { [".id"] = "*1", ["name"] = "a", ["script"] = "x" }),
                    new TikFakeDoneSentence(),
                });
            var metadata = TikEntityMetadataCache.GetMetadata<HintedRow>();
            var row = connection.LoadAll<HintedRow>().Single();
            row.Name = "b";
            row.Script = null;
            var (set, unsets) = TikConnectionExtensions.BuildUpdateCommands(connection, row, metadata, null, row.Id);
            var add = TikConnectionExtensions.BuildCreateCommand(connection, new HintedRow { Name = "c" }, metadata, null);
            var listen = TikConnectionExtensions.CreateListenCommandWithFilter<HintedRow>(connection);

            Assert.AreEqual(1, unsets.Count);
            foreach (var command in new[] { set!, unsets[0], add, listen })
            {
                var hints = command.HintsOf();
                Assert.AreSame(metadata.LabelHints, hints, command.CommandText);
                Assert.IsTrue(hints.WinboxLabels!.ContainsKey("name"), command.CommandText);
                // A listen with the read hints would run the CLI's stats, sensitive and flag reads on every poll.
                Assert.IsFalse(hints.MergeStats || hints.SerializeRead || hints.ShowSensitive || hints.FlagFields.Count > 0,
                    command.CommandText);
            }
        }

        [TestMethod]
        public void ACommandLevelLoad_SetsTheEntitysReadHints_EvenOnACommandUsedForAnotherEntity()
        {
            var connection = new TikFakeConnection()
                .WithResponse(cmd => true, _ => new ITikSentence[] { new TikFakeDoneSentence() });
            var command = connection.CreateCommand("/hinted/print");

            command.LoadList<HintedRow>();
            Assert.AreSame(Read<HintedRow>(), command.HintsOf());

            command.LoadList<PlainRow>();
            Assert.AreSame(Read<PlainRow>(), command.HintsOf(), "a reused command does not keep the first entity's hints");

            command.LoadSingleOrDefault<HintedRow>();
            Assert.AreSame(Read<HintedRow>(), command.HintsOf());
        }

        [TestMethod]
        public void ACommandLevelListen_GetsTheLabelsOnly()
        {
            var connection = new TikFakeConnection()
                .WithResponse(cmd => true, _ => new ITikSentence[] { new TikFakeDoneSentence() });
            var command = connection.CreateCommand("/hinted/listen");

            command.LoadListenWithCallback<HintedRow>(_ => { });
            command.CancelAndJoin(TimeSpan.FromSeconds(5));

            Assert.AreSame(Labels<HintedRow>(), command.HintsOf());
        }

        [TestMethod]
        public void TheMenuSchemaCacheKey_IsTheSortedNormalisedLabels()
        {
            var one = new TikTransportHints(false, false, new string[0], new string[0], false,
                new[] { new KeyValuePair<string, string>("b", "Second Label"), new KeyValuePair<string, string>("a", "First") });
            var other = new TikTransportHints(true, true, new[] { "x" }, new[] { "y" }, true,
                new[] { new KeyValuePair<string, string>("a", "First"), new KeyValuePair<string, string>("b", "Second Label") });

            Assert.AreEqual(one.WinboxLabelsKey, other.WinboxLabelsKey, "two entities with the same labels share a schema");
            StringAssert.StartsWith(one.WinboxLabelsKey, "a=");
        }

        // ── A raw command carries none ────────────────────────────────────────

        [TestMethod]
        public void ARawCommand_CarriesNoHints()
        {
            using (var transport = new HintRecordingTransport())
            {
                transport.Open("192.0.2.1", 0, "u", "p");
                var command = transport.CreateCommand("/hinted/print").WithHints(Read<HintedRow>());
                ((ITikRawCommand)command).IsRaw = true;

                command.ExecuteList();

                Assert.IsTrue(transport.Seen.Single().IsRaw);
                Assert.AreSame(TikTransportHints.None, transport.Seen.Single().Hints);
            }
        }

        // ── Every derived descriptor keeps them ───────────────────────────────

        [TestMethod]
        public void With_KeepsTheHints_AndEverythingNotReplaced()
        {
            var hints = Read<HintedRow>();
            var parameters = new List<ITikCommandParameter> { new TikCommandParameter("name", "a", TikCommandParameterFormat.Filter) };
            var d = new TikCommandDescriptor("/hinted/print", parameters, true, true, hints);

            var renamed = d.With(commandText: "/hinted/listen");
            var reworded = d.With(parameters: new List<ITikCommandParameter>());
            var rehinted = d.With(hints: TikTransportHints.None);

            Assert.AreSame(hints, renamed.Hints);
            Assert.AreSame(parameters, renamed.Parameters);
            Assert.IsTrue(renamed.IsRaw && renamed.WrapAsValue);
            Assert.AreSame(hints, reworded.Hints);
            Assert.AreEqual("/hinted/print", reworded.CommandText);
            Assert.AreSame(TikTransportHints.None, rehinted.Hints);
            Assert.AreSame(TikTransportHints.None, new TikCommandDescriptor("/x/print", parameters).Hints,
                "the public constructor yields no hints");
        }

        [TestMethod]
        public void EveryGenericCommandRun_HandsTheHintsOn()
        {
            using (var transport = new HintRecordingTransport())
            {
                transport.Open("192.0.2.1", 0, "u", "p");
                var hints = Read<HintedRow>();
                ITikCommand Hinted(string text) => transport.CreateCommand(text).WithHints(hints);

                Hinted("/hinted/print").ExecuteList();
                Hinted("/hinted/print").ExecuteList("name", "secret");     // the .proplist descriptor
                Hinted("/hinted/print").ExecuteSingleRowOrDefault();
                Hinted("/hinted/print").ExecuteListAsync().GetAwaiter().GetResult();
                Hinted("/hinted/print").ExecuteListAsync(new[] { "name" }).GetAwaiter().GetResult();
                Hinted("/hinted/print").ExecuteListUntilDone(TimeSpan.FromSeconds(1)).ToList();
                Hinted("/hinted/add").ExecuteScalar();
                Hinted("/hinted/set").ExecuteNonQuery();
                Hinted("/hinted/set").ExecuteNonQueryAsync().GetAwaiter().GetResult();

                Assert.AreEqual(9, transport.Seen.Count);
                foreach (var d in transport.Seen)
                    Assert.AreSame(hints, d.Hints, d.CommandText + " " + string.Join(" ", d.Parameters.Select(p => p.Name)));
                Assert.IsTrue(transport.Seen[1].Parameters.Any(p => p.Name == TikSpecialProperties.Proplist));
            }
        }

        [TestMethod]
        public void TheCallbackPath_AndThePolledListenAndAsyncList_KeepTheHints()
        {
            using (var transport = new HintRecordingTransport())
            {
                transport.Open("192.0.2.1", 0, "u", "p");
                var hints = Labels<HintedRow>();
                foreach (string text in new[] { "/hinted/listen", "/hinted/print" })
                {
                    var command = transport.CreateCommand(text).WithHints(hints);
                    using (var polled = new ManualResetEventSlim())
                    {
                        transport.OnPoll = polled.Set;
                        command.ExecuteWithCallback(_ => { });
                        Assert.IsTrue(polled.Wait(TimeSpan.FromSeconds(5)), text + ": no poll");
                        command.CancelAndJoin(TimeSpan.FromSeconds(5));
                    }
                }

                Assert.IsTrue(transport.Polled.Any(d => d.CommandText == "/hinted/print"));
                foreach (var d in transport.Polled)
                    Assert.AreSame(hints, d.Hints, d.CommandText);
            }
        }

        // ── Shipping code builds a parentless descriptor by naming TikTransportHints.None ──

        // Outside the two files that build a descriptor from a command (TikGenericCommand) or from another descriptor
        // (TikCommandModel's With), every 'new TikCommandDescriptor(' in a shipping project names TikTransportHints.None
        // in the same statement. So the hint-less public constructor is called nowhere in shipping code, and the next
        // derivation site cannot lose the hints without saying so — it either uses With(...) or writes None.
        [TestMethod]
        public void EveryDescriptorBuiltWithoutAParent_SaysSo()
        {
            string? repo = WikiSampleFinder.FindRepositoryRoot();
            if (repo == null)
                Assert.Inconclusive("the repository root (tik4net.sln) was not found above the test assembly");

            var exempt = new[] { "TikGenericCommand.cs", "TikCommandModel.cs" };
            var offenders = new[] { "tik4net", "tik4net.objects", "tik4net.ssh", "tik4net.testing" }
                .Select(p => Path.Combine(repo, p))
                .SelectMany(dir => Directory.EnumerateFiles(dir, "*.cs", SearchOption.AllDirectories))
                .Where(f => !f.Contains(Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar)
                            && !f.Contains(Path.DirectorySeparatorChar + "bin" + Path.DirectorySeparatorChar)
                            && !exempt.Contains(Path.GetFileName(f)))
                .SelectMany(f =>
                {
                    string text = File.ReadAllText(f);
                    return Regex.Matches(text, @"new\s+TikCommandDescriptor\s*\(").Cast<Match>()
                        .Where(m =>
                        {
                            int end = text.IndexOf(';', m.Index);
                            return !text.Substring(m.Index, (end < 0 ? text.Length : end) - m.Index).Contains("TikTransportHints.None");
                        })
                        .Select(m => f.Substring(repo.Length).TrimStart(Path.DirectorySeparatorChar) + ":"
                                     + (text.Take(m.Index).Count(c => c == '\n') + 1));
                })
                .ToList();

            Assert.AreEqual(0, offenders.Count,
                "Derive a descriptor with descriptor.With(...), or pass TikTransportHints.None to the internal constructor:\n"
                + string.Join("\n", offenders));
        }

        // ── The double ────────────────────────────────────────────────────────

        // Records every descriptor a run hands it; answers reads with no rows. Its monitors are polled table shapes, as
        // REST's, the CLI's and WinBox native's are, so a listen goes through PollingMonitorEngine.
        private sealed class HintRecordingTransport : TikCommandConnectionBase, ITikMonitorTransport, IPollingMonitorHost
        {
            public List<TikCommandDescriptor> Seen { get; } = new List<TikCommandDescriptor>();
            public List<TikCommandDescriptor> Polled { get; } = new List<TikCommandDescriptor>();
            public Action? OnPoll;

            public override TikConnectionCapability Capabilities
                => TikConnectionCapability.Crud | TikConnectionCapability.AsyncCommands | TikConnectionCapability.Listen;
            public override void Open(string host, int port, string user, string password) => SetOpened();
            public override Task OpenAsync(string host, int port, string user, string password,
                CancellationToken cancellationToken = default)
            {
                SetOpened();
                return Task.CompletedTask;
            }
            public override void Close() => SetClosed();

            private T Record<T>(TikCommandDescriptor descriptor, T answer)
            {
                lock (Seen) Seen.Add(descriptor);
                return answer;
            }

            protected override IList<TikRecordSentence> RunPrint(TikCommandDescriptor descriptor)
                => Record<IList<TikRecordSentence>>(descriptor, new List<TikRecordSentence>());
            protected override Task<IList<TikRecordSentence>> RunPrintAsync(TikCommandDescriptor descriptor, CancellationToken cancellationToken)
                => Task.FromResult(RunPrint(descriptor));
            protected override string RunAdd(TikCommandDescriptor descriptor) => Record(descriptor, "*1");
            protected override void RunNonQuery(TikCommandDescriptor descriptor) => Record(descriptor, 0);
            protected override Task RunNonQueryAsync(TikCommandDescriptor descriptor, CancellationToken cancellationToken)
                => Task.FromResult(Record(descriptor, 0));
            protected override string RunRawText(TikCommandDescriptor descriptor) => Record(descriptor, string.Empty);

            TikMonitorHandle ITikMonitorTransport.RunMonitorAsync(TikCommandDescriptor descriptor,
                Action<TikRecordSentence> onRow, Action<TikTrapSentenceResult> onError, Action onDone)
                => PollingMonitorEngine.TryStartTableShape("hints", this, descriptor, TikPath.Verb(descriptor.CommandText),
                       null, onRow, onError, onDone)
                   ?? throw new InvalidOperationException("not a table shape: " + descriptor.CommandText);

            bool IPollingMonitorHost.ClosedByCaller => false;

            IList<TikRecordSentence> IPollingMonitorHost.PollSnapshot(TikCommandDescriptor printDescriptor)
            {
                lock (Polled) Polled.Add(printDescriptor);
                OnPoll?.Invoke();
                return new List<TikRecordSentence>();
            }

            TikTrapSentenceResult IPollingMonitorHost.ToTrap(Exception ex) => TikTrapSentenceResult.FromException(ex);
        }
    }
}
