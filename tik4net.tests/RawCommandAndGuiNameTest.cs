// RawCommandAndGuiNameTest.cs — router-free unit tests for two developer-experience features:
//   A. Raw command pass-through (CreateRawCommand) — capability gating (fail-closed).
//   B. WinBox Native GUI-name addressing — WinboxHandlerMap / WinboxFieldResolver resolve a name
//      copied from the WinBox GUI (spaces/underscores/dots, any case) when UseGuiNames is opted in.
// These exercise pure resolution logic (seeds + a hand-built derived-path map), so they need no router.
//
// Note on scope: the resolver dictionaries are already case-INsensitive, so a pure case difference
// resolves regardless of the flag. What GUI-names adds on top is folding the WinBox separators
// (space / underscore → '-', abbreviation '.' dropped) on the INPUT side before lookup.

using System;
using System.Collections.Generic;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using tik4net.Testing;
using tik4net.Winbox;

namespace tik4net.tests
{
    [TestClass]
    public class RawCommandAndGuiNameTest
    {
        // ── A. Raw command pass-through — capability gating ────────────────────

        [TestMethod]
        public void CreateRawCommand_OnTransportWithoutCapability_ThrowsFailClosed()
        {
            // TikFakeConnection does not implement ITikConnectionCapabilities → supports nothing (fail-closed).
            ITikConnection conn = new TikFakeConnection();

            Assert.IsFalse(conn.Supports(TikConnectionCapability.RawCommand));
            var ex = Assert.ThrowsException<TikConnectionCapabilityNotSupportedException>(
                () => conn.CreateRawCommand("/export"));
            Assert.AreEqual(TikConnectionCapability.RawCommand, ex.Capability);
        }

        [TestMethod]
        public void CreateRawCommand_NullOrEmpty_Throws()
        {
            ITikConnection conn = new TikFakeConnection();
            Assert.ThrowsException<ArgumentNullException>(() => conn.CreateRawCommand(null));
            Assert.ThrowsException<ArgumentException>(() => conn.CreateRawCommand(""));
        }

        // ── B. WinBox handler-map GUI-name path resolution ─────────────────────

        private static WinboxHandlerMap MapWithDerived(params (string path, int[] handler)[] entries)
        {
            var map = new WinboxHandlerMap();
            var derived = new Dictionary<string, int[]>(StringComparer.OrdinalIgnoreCase);
            foreach (var (path, handler) in entries)
                derived[path] = handler;
            map.SetDerivedPaths(derived);
            return map;
        }

        [TestMethod]
        public void HandlerMap_GuiNameOff_RejectsSeparatorStyledSegment()
        {
            var map = MapWithDerived(("/interfaces/interface-list", new[] { 20, 5 }));

            CollectionAssert.AreEqual(new[] { 20, 5 }, map.Resolve("/interfaces/interface-list"));
            // A pure case difference already resolves (the derived map is case-insensitive)…
            CollectionAssert.AreEqual(new[] { 20, 5 }, map.Resolve("/Interfaces/Interface-List"));
            // …but an underscore/space inside a segment does NOT, while GUI-names is off (strict, default).
            Assert.IsNull(map.Resolve("/interfaces/interface_list"));
            Assert.IsNull(map.Resolve("/interfaces/interface list"));
        }

        [TestMethod]
        public void HandlerMap_GuiNameOn_ResolvesSeparatorStyledSegment()
        {
            var map = MapWithDerived(("/interfaces/interface-list", new[] { 20, 5 }));
            map.UseGuiNames = true;

            CollectionAssert.AreEqual(new[] { 20, 5 }, map.Resolve("/Interfaces/Interface_List"));
            CollectionAssert.AreEqual(new[] { 20, 5 }, map.Resolve("/interfaces/interface list"));
            // Still null for a genuinely unknown path.
            Assert.IsNull(map.Resolve("/interfaces/does-not-exist"));
        }

        [TestMethod]
        public void HandlerMap_GuiNameOn_ResolvesViaShippedAlias()
        {
            // The .jg-derived key is the menu-label path; the apiPath reaches it through ShippedAlias.
            var map = MapWithDerived(("/interfaces/interface-list", new[] { 20, 5 }));
            map.UseGuiNames = true;

            // /interface/list → (alias) → /interfaces/interface-list, both spellings route through the alias.
            CollectionAssert.AreEqual(new[] { 20, 5 }, map.Resolve("/interface/list"));
            CollectionAssert.AreEqual(new[] { 20, 5 }, map.Resolve("/Interface/List"));
        }

        // ── B. WinBox field-resolver GUI-name field resolution (seed-only, no catalog) ──

        private static WinboxFieldResolver Resolver(bool useGuiNames,
            IReadOnlyDictionary<string, int> overrides = null)
        {
            // No .jg catalog → only the protocol seeds resolve (.id/comment/name/disabled), which is enough
            // to verify the GUI-name normalization branch without a live router.
            return new WinboxFieldResolver("/interface", new[] { 20, 0 }, catalog: null,
                overrides: overrides, useGuiNames: useGuiNames);
        }

        [TestMethod]
        public void FieldResolver_ExactSeedName_ResolvesRegardlessOfGuiFlag()
        {
            Assert.AreEqual(WinboxM2Protocol.RecordKey.Disabled, Resolver(useGuiNames: false).ResolveKey("disabled"));
            Assert.AreEqual(WinboxM2Protocol.RecordKey.Name, Resolver(useGuiNames: false).ResolveKey("name"));
            // Case alone already resolves (seed dictionaries are case-insensitive).
            Assert.AreEqual(WinboxM2Protocol.RecordKey.Disabled, Resolver(useGuiNames: false).ResolveKey("Disabled"));
        }

        [TestMethod]
        public void FieldResolver_GuiNameOff_RejectsSeparatorStyledName()
        {
            // A GUI label still carrying its abbreviation dot / surrounding spaces is not a seed key verbatim.
            Assert.ThrowsException<WinboxFieldResolutionException>(
                () => Resolver(useGuiNames: false).ResolveKey("disabled."));
            Assert.ThrowsException<WinboxFieldResolutionException>(
                () => Resolver(useGuiNames: false).ResolveKey(" name "));
        }

        [TestMethod]
        public void FieldResolver_GuiNameOn_ResolvesSeparatorStyledName()
        {
            // The label normalizer (dot dropped, whitespace trimmed/folded) reduces these to the seed name.
            Assert.AreEqual(WinboxM2Protocol.RecordKey.Disabled, Resolver(useGuiNames: true).ResolveKey("disabled."));
            Assert.AreEqual(WinboxM2Protocol.RecordKey.Name, Resolver(useGuiNames: true).ResolveKey(" name "));
        }

        [TestMethod]
        public void FieldResolver_OverrideWinsOverGuiNormalization()
        {
            // FieldOverride is checked first (exact key), so it is never bypassed by GUI normalization.
            var overrides = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase) { ["My_Field"] = 0x12345 };
            Assert.AreEqual(0x12345, Resolver(useGuiNames: true, overrides).ResolveKey("My_Field"));
        }
    }
}
