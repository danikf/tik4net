using System;
using System.Threading;
using System.Threading.Tasks;

namespace tik4net.WinboxNative
{
    /// <summary>
    /// <see cref="TikConnectionSetup"/> factories for the WinBox native M2 transport, kept in this transport's own
    /// namespace beside the connection they create.
    /// </summary>
    /// <remarks>
    /// Every option comes from the setup — these forward to
    /// <see cref="TikConnectionSetup.Create(TikConnectionType, Action{ITikConnection})"/> rather than
    /// building a connection by hand, so a new option reaches this transport without anyone remembering to
    /// copy it. Add a <c>using tik4net.WinboxNative;</c> to see them.
    /// </remarks>
    public static class WinboxNativeConnectionSetupExtensions
    {
        /// <summary>
        /// Creates and opens a WinBox <b>native-M2</b> connection (encrypted TCP port 8291). Issues
        /// structured M2 CRUD calls (no terminal), translating API paths/field names to/from WinBox handler
        /// and field keys via the router's version-matched <c>.jg</c> catalog. Requires the <c>winbox</c>
        /// service to be enabled (default).
        /// <para><b>Experimental.</b> That catalog mapping is reconstructed rather than published, so
        /// translating RouterOS API syntax into M2 — which addresses everything by number — is not a
        /// straightforward one: common tables are covered, an exotic table or verb may need one of the
        /// mappings below. <b>For production work prefer
        /// <c>CreateWinboxCliConnection</c> (<c>tik4net.WinboxCli</c>)</b>, the stable, proven transport on the same encrypted
        /// channel, which drives the router's own CLI and needs no name mapping at all. See
        /// <see cref="WinboxNativeConnection"/> and the wiki page <i>WinBox-Native-connection</i>.</para>
        /// </summary>
        /// <param name="setup">The configured connection setup.</param>
        /// <param name="configure">
        /// Optional hook to configure the connection <b>before it opens</b> — the place to register
        /// <see cref="WinboxNativeConnection.PathAlias"/> / <see cref="WinboxNativeConnection.FieldOverride"/>
        /// mappings or set <see cref="WinboxNativeConnection.CatalogCachePath"/>. These must be set before
        /// <c>Open</c>, which is why this factory exposes a callback rather than only returning the connection.
        /// </param>
        /// <example>
        /// <para>The mappings are written in the <b>labels WinBox shows you</b>, not in raw handler numbers.
        /// Open the window in WinBox, read its menu breadcrumb and field captions, and lower-case them with
        /// spaces as dashes:</para>
        /// <code>
        /// using var conn = setup.CreateWinboxNativeConnection(c =>
        /// {
        ///     // WinBox menu:  PPP ▸ Secrets ▸ (window) PPP Secret     API path: /ppp/secret
        ///     c.PathAlias("/ppp/secret", "/ppp/secrets/ppp-secret");
        ///
        ///     // Accept field captions as typed in the GUI ("MAC Address" → mac-address, "Dst. Address" → dst-address).
        ///     c.UseGuiNames = true;
        ///
        ///     // Escape hatches, only when the label route fails:
        ///     c.FieldOverride("/ip/hotspot/user", "mac-address", 0x1);   // pin one field to its M2 key
        ///     c.PathOverride("/tool/sniffer", new[] { 27, 101 });        // pin a whole path to its handler
        /// });
        /// </code>
        /// <para><see cref="WinboxNativeConnection.PathAlias"/> keeps working after a RouterOS upgrade (only the
        /// text is pinned; the handler number is read live from the router's <c>.jg</c> catalog), whereas the
        /// numeric <c>*Override</c> forms pin values that can move between versions.</para>
        /// </example>
        public static ITikWinboxNativeConnection CreateWinboxNativeConnection(this TikConnectionSetup setup, Action<WinboxNativeConnection>? configure = null)
            => (ITikWinboxNativeConnection)setup.Create(TikConnectionType.WinboxNative, TikConnectionSetup.Typed(configure));

        /// <summary>Async version of <see cref="CreateWinboxNativeConnection"/>.</summary>
        public static async Task<ITikWinboxNativeConnection> CreateWinboxNativeConnectionAsync(this TikConnectionSetup setup, 
            Action<WinboxNativeConnection>? configure = null, CancellationToken ct = default)
        {
            return (ITikWinboxNativeConnection)await setup.CreateAsync(TikConnectionType.WinboxNative, TikConnectionSetup.Typed(configure), ct).ConfigureAwait(false);
        }
    }
}
