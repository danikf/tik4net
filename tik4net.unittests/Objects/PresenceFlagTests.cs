using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using tik4net.Objects;
using tik4net.Objects.Routing;

namespace tik4net.unittests.Objects
{
    /// <summary>
    /// G3.1: a field RouterOS stores as a valueless PRESENCE FLAG is reported by the binary API and REST as
    /// <c>name=</c> — the word present, the value empty — and omitted entirely when the flag is clear.
    /// Parsed as an ordinary bool, that empty value is <c>false</c>, so the field read back as
    /// <c>false</c> whatever the router said.
    /// </summary>
    /// <remarks>
    /// Measured on the lab CHR (RouterOS 7.24) before the fix, on the same row read four ways:
    /// binary API <c>=fib=</c>, REST <c>"fib": ""</c>, Telnet <c>fib=true</c>, WinboxNative
    /// <c>fib=true</c>. So the CLI and native transports were never wrong and needed no change — the
    /// marker makes the API and REST agree with them.
    /// </remarks>
    [TestClass]
    public class PresenceFlagTests
    {
        [TikEntity("/test/flag-entity")]
        internal class FlagEntity
        {
            [TikProperty("fib", IsPresenceFlag = true)]
            public bool? Fib { get; set; }

            [TikProperty("plain")]
            public bool? Plain { get; set; }
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
            Assert.AreEqual(true, entity.Fib,
                "the binary API spells a set presence flag as an empty value — reading it as false loses the state");
        }

        /// <summary>The marker must not leak: an ordinary bool's empty value is still false.</summary>
        [TestMethod]
        public void AnEmptyValueOnAnOrdinaryBoolIsStillFalse()
        {
            var entity = new FlagEntity();
            Accessor("Plain").SetEntityValue(entity, "");
            Assert.AreEqual(false, entity.Plain);
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
            Assert.AreEqual(true, entity.Fib);

            Accessor("Fib").SetEntityValue(entity, "no");
            Assert.AreEqual(false, entity.Fib);

            entity.Fib = true;
            Assert.AreEqual("yes", Accessor("Fib").GetEntityValue(entity));
        }

        /// <summary>
        /// Absence is not falsehood. A row that does not carry the word leaves a nullable property
        /// <c>null</c> — the router reported nothing, and the mapper does not invent a value that
        /// <c>Save</c> would then have to decide about.
        /// </summary>
        [TestMethod]
        public void AnAbsentPresenceFlagStaysNull()
        {
            var entity = new FlagEntity();
            Accessor("Fib").SetEntityValue(entity, null);
            Assert.IsNull(entity.Fib);
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
            Assert.AreEqual(true, table.Fib, "the router's 'main' table has fib set, and reports it as 'fib='");
        }
    }
}
