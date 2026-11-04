namespace tik4net.Objects.System
{
    /// <summary>
    /// A policy a script or a scheduler event runs under, a member of <see cref="SystemScript.Policy"/> and
    /// <see cref="SystemScheduler.Policy"/>.
    /// </summary>
    /// <remarks>
    /// A subset of <see cref="User.UserPolicy"/>: the login policies (<c>local</c>, <c>ssh</c>, <c>api</c>, …) are
    /// refused here with <c>input does not match any value of policy</c>. <see cref="Dude"/> is RouterOS 6 only.
    /// </remarks>
    public enum SystemScriptPolicy
    {
        /// <summary>ftp — read, write and delete files.</summary>
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
        /// <summary>password — change passwords.</summary>
        [TikEnum("password")] Password,
        /// <summary>sniff — run the packet sniffer.</summary>
        [TikEnum("sniff")] Sniff,
        /// <summary>sensitive — see sensitive values (passwords, keys).</summary>
        [TikEnum("sensitive")] Sensitive,
        /// <summary>romon — connect to RoMON.</summary>
        [TikEnum("romon")] Romon,
        /// <summary>dude — The Dude (RouterOS 6).</summary>
        [TikEnum("dude")] Dude,
    }
}
