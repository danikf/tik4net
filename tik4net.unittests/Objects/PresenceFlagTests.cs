using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using tik4net.Objects;
using tik4net.Objects.Routing;

namespace tik4net.unittests.Objects
{
    /// <summary>
    /// A field RouterOS stores as a valueless PRESENCE FLAG is reported by the binary API and REST as
    /// <c>name=</c> — the word present, the value empty — and omitted entirely when the flag is clear.
    /// </summary>
    /// <remarks>
    /// Measured on the lab CHR (RouterOS 7.24), on the same row read four ways: binary API <c>=fib=</c>,
    /// REST <c>"fib": ""</c>, Telnet <c>fib=true</c>, WinboxNative <c>fib=true</c>. The marker makes the API and
    /// REST agree with the rest.
    /// </remarks>
    [TestClass]
    public class PresenceFlagTests
    {
        [TikEntity("/test/flag-entity")]
        internal class FlagEntity
        {
            [TikProperty("fib", IsPresenceFlag = true)]
            public TikField<bool?> Fib { get; set; }

            [TikProperty("plain")]
            public TikField<bool?> Plain { get; set; }
        }

        private static TikEntityPropertyAccessor Accessor(string propertyName)
            => TikEntityMetadataCache.GetMetadata<FlagEntity>()
                .Properties.Single(p => p.PropertyName == propertyName);

        /// <summary>The whole point: <c>fib=</c> is the router saying yes, not saying nothing.</summary>
        [TestMethod]
        public void AnEmptyValueOnAPresenceFlagReadsAsTrue()
        {
            var entity = new FlagEntity();
            Accessor("Fib").SetEntityValue(entity, "");
            Assert.IsTrue(entity.Fib == true,
                "the binary API spells a set presence flag as an empty value — reading it as anything else loses the state");
        }

        /// <summary>
        /// The marker must not leak, and an empty value on an unmarked bool must not vanish either: if the router really
        /// prints one, being printed may be its value, so it stays visible rather than reading as "nothing".
        /// </summary>
        [TestMethod]
        public void AnEmptyValueOnAnOrdinaryBoolStaysTheRoutersWord()
        {
            var entity = new FlagEntity();
            Accessor("Plain").SetEntityValue(entity, "");
            Assert.AreEqual(TikFieldState.Unparsed, entity.Plain.State);
            Assert.AreEqual("", entity.Plain.RawValue);
        }

        /// <summary>
        /// The transports that spell it out keep working — the CLI and WinboxNative both send
        /// <c>true</c>, and the write direction still goes out as the <c>yes</c> the router accepts.
        /// </summary>
        [TestMethod]
        public void APresenceFlagStillReadsTheSpelledOutFormAndWritesYes()
        {
            var entity = new FlagEntity();
            Accessor("Fib").SetEntityValue(entity, "true");
            Assert.IsTrue(entity.Fib == true);

            Accessor("Fib").SetEntityValue(entity, "no");
            Assert.IsTrue(entity.Fib == false);

            entity.Fib = true;
            Assert.AreEqual("yes", Accessor("Fib").GetEntityValue(entity));
        }

        /// <summary>
        /// Absence is not falsehood. A row that does not carry the word reads Absent — the router reported
        /// nothing, and the mapper does not invent a value that <c>Save</c> would then have to decide about.
        /// </summary>
        [TestMethod]
        public void AnAbsentPresenceFlagStaysAbsent()
        {
            var entity = new FlagEntity();
            Accessor("Fib").SetEntityValue(entity, null);
            Assert.AreEqual(TikFieldState.Absent, entity.Fib.State);
        }

        /// <summary>The live property this exists for is actually marked.</summary>
        [TestMethod]
        public void RoutingTableFibIsDeclaredAPresenceFlag()
        {
            var fib = TikEntityMetadataCache.GetMetadata<RoutingTable>()
                .Properties.Single(p => p.PropertyName == nameof(RoutingTable.Fib));
            Assert.IsTrue(fib.IsPresenceFlag);

            var table = new RoutingTable();
            fib.SetEntityValue(table, "");
            Assert.IsTrue(table.Fib == true, "the router's 'main' table has fib set, and reports it as 'fib='");
        }
    }
}
