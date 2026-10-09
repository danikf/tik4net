namespace tik4net.Cli
{
    /// <summary>
    /// The router a connection continues into through the agent it logged in to — over RoMON SSH for the CLI
    /// transports (<see cref="RouterOsCliLogin.RomonSshLoginAsync"/>), over the WinBox relay for the WinBox ones
    /// (<see cref="Winbox.WinboxRomonChannel"/>). Built by <see cref="TikConnectionSetup.ApplyTo"/> from
    /// <see cref="TikConnectionSetup.RomonAgentSetup"/>; the host, port and credentials the connection is then
    /// opened with are the agent's.
    /// </summary>
    /// <remarks>Nothing here is ever logged: <see cref="Password"/> is the target's.</remarks>
    internal sealed class RomonRelayTarget
    {
        internal RomonRelayTarget(string romonId, string user, string password, TikRomonAgentSetup? agent = null)
        {
            RomonId = RouterOsCliLogin.NormalizeRomonId(romonId);
            User = user;
            Password = password;
            Agent = agent;
        }

        /// <summary>The target's RoMON id (its <c>/tool romon</c> <c>current-id</c>), normalised.</summary>
        internal string RomonId { get; }

        internal string User { get; }

        internal string Password { get; }

        /// <summary>The agent, for <see cref="TikRomonConnectionInfo"/>; <c>null</c> when a probe set the target by hand.</summary>
        internal TikRomonAgentSetup? Agent { get; }

        public override string ToString() => "RoMON " + RomonId + " as " + User;
    }
}
