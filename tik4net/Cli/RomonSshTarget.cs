namespace tik4net.Cli
{
    /// <summary>
    /// The router a CLI connection continues into, through the agent it logged in to, over RoMON SSH
    /// (<see cref="RouterOsCliLogin.RomonSshLoginAsync"/>).
    /// </summary>
    /// <remarks>
    /// Internal while the public RoMON surface is being designed (see the RoMON plan: <c>RomonAgentSetup</c>
    /// on <see cref="TikConnectionSetup"/>, <c>GetRomonConnectionInfo()</c>). Nothing here is ever logged:
    /// <see cref="Password"/> is the target's.
    /// </remarks>
    internal sealed class RomonSshTarget
    {
        internal RomonSshTarget(string romonId, string user, string password)
        {
            RomonId = RouterOsCliLogin.NormalizeRomonId(romonId);
            User = user;
            Password = password;
        }

        /// <summary>The target's RoMON id (its <c>/tool romon</c> <c>current-id</c>), normalised.</summary>
        internal string RomonId { get; }

        internal string User { get; }

        internal string Password { get; }

        public override string ToString() => "RoMON " + RomonId + " as " + User;
    }
}
