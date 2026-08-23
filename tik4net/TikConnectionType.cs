namespace tik4net
{
    /// <summary>
    /// Connection type used to access mikrotik router. Default is <see cref="TikConnectionType.Api"/>.
    /// </summary>
    public enum TikConnectionType
    {
        /// <summary>
        /// Mikrotik API connection - default value.
        /// </summary>
        Api,
        /// <summary>
        /// Mikrotik API-SSL connection (supports only mode with certificate on mikrotik). See https://github.com/danikf/tik4net/wiki/SSL-connection for details.
        /// </summary>
        ApiSsl,
        /// <summary>
        /// MikroTik REST API over HTTP (port 80). Requires RouterOS 7.1+. No Listen/Streaming support.
        /// </summary>
        Rest,
        /// <summary>
        /// MikroTik REST API over HTTPS (port 443). Requires RouterOS 7.1+ with www-ssl service and a certificate.
        /// </summary>
        RestSsl,
        /// <summary>
        /// MikroTik RouterOS SSH connection (TCP port 22).
        /// Provides full CRUD support via the RouterOS CLI over an SSH PTY shell
        /// (<c>print as-value</c>); Listen and Safe Mode are supported (polling), Streaming is not.
        /// The implementation lives in the satellite NuGet package <c>tik4net.ssh</c> (separate because
        /// of its <c>Renci.SshNet</c> dependency). Create it via <c>setup.CreateSshConnection()</c> or
        /// <c>new tik4net.Ssh.SshConnection()</c>; to use it through <see cref="ConnectionFactory"/> call
        /// <c>tik4net.Ssh.Tik4NetSsh.Register()</c> once at startup.
        /// Requires the <c>ssh</c> service to be enabled on the router (<c>/ip/service set ssh disabled=no</c>).
        /// </summary>
        Ssh,
        /// <summary>
        /// MikroTik RouterOS Telnet connection (plain-text TCP port 23).
        /// Provides full CRUD support via the RouterOS CLI (<c>print as-value</c>).
        /// Listen/Streaming operations are not supported.
        /// Requires the <c>telnet</c> service to be enabled on the router (<c>/ip/service set telnet disabled=no</c>).
        /// </summary>
        Telnet,
        /// <summary>
        /// MikroTik RouterOS MAC-Telnet connection (UDP port 20561).
        /// Provides full CRUD support via the RouterOS CLI over the MAC layer (EC-SRP5 auth).
        /// Listen/Streaming operations are not supported.
        /// Requires <c>/tool/mac-server set allowed-interface-list=all</c> on the router.
        /// The router's MAC address is discovered via MNDP (up to 5 s) unless
        /// <see cref="MacTelnet.MacTelnetConnection.RouterMac"/> is set before opening.
        /// </summary>
        MacTelnet,
        /// <summary>
        /// MikroTik RouterOS WinBox CLI connection (TCP port 8291).
        /// Provides full CRUD support by driving the RouterOS CLI over the encrypted WinBox channel
        /// (EC-SRP5 auth, AES-128-CBC) via the <c>mepty</c> terminal handler.
        /// Listen/Streaming operations are not supported.
        /// Requires the <c>winbox</c> service to be enabled on the router (default).
        /// This is the terminal-driven WinBox mode; native-M2 and MAC-layer WinBox modes are planned
        /// separately.
        /// </summary>
        WinboxCli,
        /// <summary>
        /// MikroTik RouterOS WinBox CLI connection over the MAC layer (UDP port 20561,
        /// <c>client_type=0x0f90</c>). Same encrypted WinBox terminal CLI as <see cref="WinboxCli"/>,
        /// but M2 messages travel over the MAC layer — so it works without an IP route to the router.
        /// Listen/Streaming operations are not supported.
        /// Requires <c>/tool/mac-server/mac-winbox set allowed-interface-list=all</c> on the router.
        /// The router's MAC address is discovered via MNDP (up to 5 s) unless
        /// <see cref="WinboxCliMac.WinboxCliMacConnection.RouterMac"/> is set before opening.
        /// </summary>
        WinboxCliMac,
        /// <summary>
        /// MikroTik RouterOS WinBox <b>native-M2</b> connection (TCP port 8291).
        /// Performs full CRUD as structured M2 <c>getall</c>/<c>get-one</c>/<c>set</c>/<c>add</c>/<c>remove</c>/<c>move</c>
        /// calls (no terminal), translating numeric WinBox field keys to/from RouterOS API field names via a
        /// version-matched <c>.jg</c> catalog, so the O/R mapper works unchanged.
        /// Listen/Streaming are not supported.
        /// Requires the <c>winbox</c> service to be enabled on the router (default).
        /// <para><b>Experimental.</b> M2 addresses everything by number, and the API name ↔ WinBox key
        /// mapping is reconstructed from the router's own <c>.jg</c> catalog rather than being a published
        /// contract, so translating RouterOS API syntax into it is not a straightforward one: it covers
        /// common tables, but an exotic table or verb may need a session mapping, and mapping details may
        /// still change between releases. <b>For production work prefer <see cref="WinboxCli"/></b> — the
        /// stable, proven transport on the same encrypted channel, which drives the router's own CLI and so
        /// needs no name mapping at all. The two are interchangeable at the <c>ITikConnection</c> level.
        /// See the wiki page <i>WinBox-Native-connection</i> ("The mapping problem").</para>
        /// </summary>
        WinboxNative,
        /// <summary>
        /// MikroTik RouterOS WinBox <b>native-M2</b> connection over the MAC layer (UDP port 20561,
        /// <c>client_type=0x0f90</c>). Same structured M2 <c>getall</c>/<c>get-one</c>/<c>set</c>/<c>add</c>/<c>remove</c>/<c>move</c>
        /// CRUD as <see cref="WinboxNative"/>, but M2 messages travel over the MAC layer — so it works
        /// without an IP route to the router.
        /// Listen/Streaming are not supported.
        /// Requires <c>/tool/mac-server/mac-winbox set allowed-interface-list=all</c> on the router.
        /// The router's MAC address is discovered via MNDP (up to 5 s) unless
        /// <see cref="WinboxNativeMac.WinboxNativeMacConnection.RouterMac"/> is set before opening.
        /// <para><b>Experimental</b>, for the same reason as <see cref="WinboxNative"/>: the API name ↔
        /// WinBox key mapping is reconstructed from the router's <c>.jg</c> catalog, not published, so the
        /// translation from RouterOS API syntax is not a straightforward one. <b>For production work prefer
        /// <see cref="WinboxCliMac"/></b> — the stable, proven transport on the same encrypted channel and
        /// the same MAC carrier, which needs no name mapping at all. See the wiki page
        /// <i>WinBox-Native-MAC-connection</i>.</para>
        /// </summary>
        WinboxNativeMac
    }
}
