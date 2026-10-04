// WinboxLabelHintTests.cs — router-free tests for the entity's WinBox labels (TikPropertyAttribute.WinboxLabel,
// carried to the native transport by the .winbox-labels marker). Priority: session field override → label → the
// name heuristic; a label this catalog does not have falls through.

#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using tik4net.Objects;
using tik4net.Testing;
using tik4net.Winbox;

namespace tik4net.unittests.Winbox
{
    [TestClass]
    public class WinboxLabelHintTests
    {
        // A window whose label no heuristic turns into the API name: 'Upstream Sync' is the API's 'relay'.
        private const string Window =
            "[{name:'Box',type:'map',path:[ 9,9 ],c:[" +
            "{name:'Name',type:'string',id:'s1'}," +
            "{name:'Upstream Sync',type:'bool',id:'b5'}," +
            "{name:'Type',type:'enm',id:'u2',values:{type:'static',map:['memory','disk']}}," +
            "{type:'deck',panes:[" +
              "{vals:[ 0 ],c:[{name:'Stop on Full',type:'bool',id:'b4'}]}," +
              "{vals:[ 1 ],c:[{name:'Stop on Full',type:'bool',id:'b6'}]}]," +
              "selon:'Type'}]}]";

        private static readonly int[] Handler = { 9, 9 };

        private static WinboxFieldResolver Resolver(string? marker, Dictionary<string, int>? overrides = null)
        {
            var catalog = new WinboxJgCatalog();
            Assert.IsTrue(catalog.TryParseInto(Window));
            return new WinboxFieldResolver("/box", Handler, catalog, overrides ?? new Dictionary<string, int>(),
                labelHints: WinboxFieldResolver.ParseLabelHints(marker));
        }

        [TestMethod]
        public void ALabel_NamesAFieldTheHeuristicCannot()
        {
            Assert.ThrowsException<WinboxFieldResolutionException>(() => Resolver(null).ResolveKey("relay"));

            var r = Resolver("relay=Upstream Sync");
            Assert.AreEqual(0x5, r.ResolveKey("relay"));
            Assert.AreEqual("relay", r.BuildKeyToApiName()[0x5], "the decode reports the field under the API name too");
        }

        [TestMethod]
        public void AQualifiedLabel_PicksOneOfTwoFieldsSharingTheLabel()
            => Assert.AreEqual(0x6, Resolver("stop=disk: Stop on Full").ResolveKey("stop"));

        [TestMethod]
        public void ASessionOverride_StillWins()
            => Assert.AreEqual(0x1, Resolver("relay=Upstream Sync", new Dictionary<string, int> { ["relay"] = 0x1 }).ResolveKey("relay"));

        [TestMethod]
        public void ALabelThisCatalogDoesNotHave_FallsThroughToTheHeuristic()
            => Assert.AreEqual(0x1, Resolver("name=Title Of Another Version").ResolveKey("name"));

        [TikEntity("/box")]
        public class LabelledBox
        {
            [TikProperty(".id", IsReadOnly = true, IsMandatory = true)]
            public string? Id { get; private set; }

            [TikProperty("relay", WinboxLabel = "Upstream Sync")]
            public TikField<bool?> Relay { get; set; }

            [TikProperty("name")]
            public TikField<string?> Name { get; set; }
        }

        private static TikFakeConnection LoadAndAdd(TikConnectionCapability capabilities)
        {
            var connection = new TikFakeConnection { Capabilities = capabilities }
                .WithResponse(cmd => cmd.First() == "/box/print", _ => new ITikSentence[] { new TikFakeDoneSentence() })
                .WithScalarResponse(cmd => cmd.First() == "/box/add", "*1");
            connection.LoadAll<LabelledBox>().ToList();
            connection.Save(new LabelledBox { Name = "x", Relay = true });
            return connection;
        }

        [TestMethod]
        public void TheMapper_SendsTheLabels_ToAConnectionThatDeclaresFieldLabels()
        {
            var connection = LoadAndAdd(new TikFakeConnection().Capabilities | TikConnectionCapability.FieldLabels);
            foreach (var verb in new[] { "/box/print", "/box/add" })
                CollectionAssert.Contains(connection.SentCommands.Single(c => c.First() == verb).ToArray(),
                    "=.winbox-labels=relay=Upstream Sync", verb);
        }

        [TestMethod]
        public void TheMapper_SendsNoLabels_ToAnyOtherConnection()
        {
            // A caller's own test double sees the same commands whether or not the entity carries labels.
            var connection = LoadAndAdd(new TikFakeConnection().Capabilities);
            Assert.IsFalse(connection.SentCommands.SelectMany(c => c).Any(w => w.Contains(".winbox-labels")));
        }
    }
}
