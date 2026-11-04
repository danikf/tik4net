namespace tik4net.Objects.User
{
    /// <summary>A user group policy, a member of <see cref="UserGroup.Policy"/>.</summary>
    /// <remarks>
    /// <see cref="Dude"/> and <see cref="Tikapp"/> are RouterOS 6 policies, <see cref="RestApi"/> a RouterOS 7 one. A
    /// script or scheduler event runs under a narrower set, <see cref="System.SystemScriptPolicy"/>.
    /// </remarks>
    public enum UserPolicy
    {
        /// <summary>local — log in from the local console.</summary>
        [TikEnum("local")] Local,
        /// <summary>telnet — log in over Telnet.</summary>
        [TikEnum("telnet")] Telnet,
        /// <summary>ssh — log in over SSH.</summary>
        [TikEnum("ssh")] Ssh,
        /// <summary>ftp — log in over FTP; read, write and delete files.</summary>
        [TikEnum("ftp")] Ftp,
        /// <summary>reboot — reboot the router.</summary>
        [TikEnum("reboot")] Reboot,
        /// <summary>read — read the configuration.</summary>
        [TikEnum("read")] Read,
        /// <summary>write — change the configuration, except users and groups.</summary>
        [TikEnum("write")] Write,
        /// <summary>policy — manage users and groups.</summary>
        [TikEnum("policy")] Policy,
        /// <summary>test — run ping, traceroute, bandwidth test and similar tools.</summary>
        [TikEnum("test")] Test,
        /// <summary>winbox — log in with WinBox.</summary>
        [TikEnum("winbox")] Winbox,
        /// <summary>password — change the user's own password.</summary>
        [TikEnum("password")] Password,
        /// <summary>web — log in with WebFig.</summary>
        [TikEnum("web")] Web,
        /// <summary>sniff — run the packet sniffer.</summary>
        [TikEnum("sniff")] Sniff,
        /// <summary>sensitive — see sensitive values (passwords, keys).</summary>
        [TikEnum("sensitive")] Sensitive,
        /// <summary>api — log in over the binary API.</summary>
        [TikEnum("api")] Api,
        /// <summary>romon — connect to RoMON.</summary>
        [TikEnum("romon")] Romon,
        /// <summary>rest-api — log in over the REST API (RouterOS 7).</summary>
        [TikEnum("rest-api")] RestApi,
        /// <summary>dude — log in to The Dude (RouterOS 6).</summary>
        [TikEnum("dude")] Dude,
        /// <summary>tikapp — log in with the MikroTik mobile app (RouterOS 6).</summary>
        [TikEnum("tikapp")] Tikapp,
    }
}
