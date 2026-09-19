using System;

namespace tik4net
{
    /// <summary>
    /// The RoMON agent a connection goes through: the router it logs in to first, and from whose shell it
    /// continues into the target over RoMON. Set it as <see cref="TikConnectionSetup.RomonAgentSetup"/> on the
    /// target's setup.
    /// </summary>
    /// <remarks>
    /// <para>
    /// It carries only <b>where the agent is and who logs in to it</b>. Everything about the session you
    /// actually use — timeouts, encoding, paging, cancellation — belongs to the target's
    /// <see cref="TikConnectionSetup"/>, because that session is the target's. This is a separate type rather
    /// than a second <see cref="TikConnectionSetup"/> so that there is nothing here that would be set and then
    /// silently ignored.
    /// </para>
    /// <para>
    /// The connection to the agent is bounded by the target setup's
    /// <see cref="TikConnectionSetup.ConnectTimeout"/> — it is the only connect there is; the relay into the
    /// target runs under its <see cref="TikConnectionSetup.ReceiveTimeout"/>.
    /// </para>
    /// </remarks>
    /// <example>
    /// <code>
    /// var agentSetup = new TikRomonAgentSetup("192.168.88.1", "admin", "agent-pw");
    /// var targetSetup = new TikConnectionSetup(TikRouterAddress.FromRomonId("AA:BB:CC:DD:EE:FF"), "admin", "target-pw")
    /// {
    ///     RomonAgentSetup = agentSetup,
    /// };
    /// using var connection = targetSetup.Create(TikConnectionType.Ssh);
    /// </code>
    /// </example>
    public sealed class TikRomonAgentSetup
    {
        /// <summary>Where the agent is: a host for Telnet and SSH; a MAC, a host, or both for MAC-Telnet.</summary>
        public TikRouterAddress Address { get; }

        /// <summary>RouterOS user name on the agent.</summary>
        public string User { get; }

        /// <summary>Password for <see cref="User"/> on the agent (may be empty).</summary>
        public string Password { get; }

        /// <summary>The agent's port for the transport used, or <c>null</c> for the transport's default.</summary>
        public int? Port { get; set; }

        /// <summary>Creates the agent's coordinates and credentials.</summary>
        /// <param name="address">Where the agent is — a host name or IP address for Telnet and SSH; a MAC
        /// (<see cref="TikRouterAddress.FromMac"/>) or a host for MAC-Telnet.</param>
        /// <param name="user">RouterOS user name on the agent.</param>
        /// <param name="password">Password for <paramref name="user"/> (may be empty).</param>
        /// <exception cref="ArgumentException"><paramref name="address"/> is empty or is itself a RoMON id —
        /// an agent is reached directly, never through another agent.</exception>
        public TikRomonAgentSetup(TikRouterAddress address, string user, string password)
        {
            if (address.IsEmpty)
                throw new ArgumentException("The agent's address must carry a host name / IP address or a MAC address.",
                    nameof(address));
            if (address.HasRomonId)
                throw new ArgumentException(
                    "An agent is reached directly, not over RoMON: give its host or MAC address, not a RoMON id. " +
                    "RoMON itself carries a session across several hops, so a second agent is never needed.",
                    nameof(address));
            Guard.ArgumentNotNull(user, nameof(user));
            Guard.ArgumentNotNull(password, nameof(password));
            Address = address;
            User = user;
            Password = password;
        }

        /// <summary>The agent's address and user, for diagnostics — never the password.</summary>
        public override string ToString() => "RoMON agent " + Address + " as " + User;
    }
}
