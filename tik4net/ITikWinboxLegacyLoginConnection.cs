namespace tik4net
{
    /// <summary>
    /// Implemented by the WinBox native transport, whose TCP carrier knows the pre-6.43 MD5 login: the opt-in to that
    /// unencrypted login (<see cref="TikConnectionSetup.AllowLegacyWinboxLogin"/>).
    /// </summary>
    /// <remarks>
    /// <see cref="TikConnectionSetup"/> applies the option through this interface. The WinBox CLI transports do not
    /// implement it: the WinBox terminal is opened only on an encrypted session, so a legacy login could not carry it.
    /// <see cref="WinboxNativeMac.WinboxNativeMacConnection"/> inherits it from the TCP connection and has no use for it:
    /// the MAC-layer carrier has no legacy login, and refuses a router without EC-SRP5 whatever the option says.
    /// </remarks>
    public interface ITikWinboxLegacyLoginConnection
    {
        /// <summary>
        /// When <c>true</c>, a router that does not answer the EC-SRP5 login is logged in to with the pre-6.43 MD5
        /// challenge–response instead, and <b>the whole session then runs in clear</b>. Default <c>false</c>. Must be set
        /// before the connection is opened.
        /// </summary>
        /// <remarks>
        /// <para>The MD5 login exists only on RouterOS before 6.43, below the 6.49 that tik4net supports. Its response can
        /// be cracked offline, and nothing after it is encrypted: anyone on the path reads the session and can change
        /// it.</para>
        /// <para>Off, a router that answers the EC-SRP5 opening frame with anything else, or with silence, fails
        /// <c>Open</c> with a <see cref="TikConnectionLoginException"/> naming EC-SRP5 and this option, and nothing is
        /// downgraded. On, the same silence also selects the legacy login, so a man in the middle who drops the EC-SRP5
        /// reply turns the session into clear text: set it only for a pre-6.43 router on a network you trust.</para>
        /// </remarks>
        bool AllowLegacyWinboxLogin { get; set; }
    }
}
