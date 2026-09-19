using System;

namespace tik4net
{
    /// <summary>How a RoMON connection is carried from the agent to the target.</summary>
    public enum TikRomonRelay
    {
        /// <summary>
        /// <c>/tool romon ssh</c> run on the agent's shell: the agent opens an SSH session to the target over the
        /// RoMON overlay and the connection continues inside it. The agent is the SSH client, so it sees the
        /// session in clear; the overlay hop itself is SSH-encrypted.
        /// </summary>
        Ssh,
    }

    /// <summary>
    /// How a connection reached its router through a RoMON agent — see
    /// <see cref="TikRomonConnectionExtensions.GetRomonConnectionInfo"/>. A snapshot taken when the connection
    /// opened; it never carries a password.
    /// </summary>
    public sealed class TikRomonConnectionInfo
    {
        internal TikRomonConnectionInfo(TikRomonRelay relay, TikRomonAgentInfo agent, TikRomonTargetInfo target)
        {
            Relay = relay;
            Agent = agent;
            Target = target;
        }

        /// <summary>How the agent carries the connection to the target.</summary>
        public TikRomonRelay Relay { get; }

        /// <summary>The router the connection logged in to first.</summary>
        public TikRomonAgentInfo Agent { get; }

        /// <summary>The router the connection's commands run on.</summary>
        public TikRomonTargetInfo Target { get; }

        /// <inheritdoc/>
        public override string ToString() => Target + " via " + Agent + " (" + Relay + " relay)";
    }

    /// <summary>The RoMON agent side of a <see cref="TikRomonConnectionInfo"/>.</summary>
    public sealed class TikRomonAgentInfo
    {
        internal TikRomonAgentInfo(TikRouterAddress address, TikConnectionType connectionType, string user, string romonId)
        {
            Address = address;
            ConnectionType = connectionType;
            User = user;
            RomonId = romonId;
        }

        /// <summary>Where the agent was reached.</summary>
        public TikRouterAddress Address { get; }

        /// <summary>The transport the connection used to reach the agent.</summary>
        public TikConnectionType ConnectionType { get; }

        /// <summary>The user the connection logged in to the agent as.</summary>
        public string User { get; }

        /// <summary>
        /// The agent's own RoMON id (its <c>/tool romon</c> <c>current-id</c>), read when the connection opened.
        /// It is what the target reports as <c>by-romon</c> in <c>/user/active</c> — the way to find this session
        /// in the target's own records.
        /// </summary>
        public string RomonId { get; }

        /// <inheritdoc/>
        public override string ToString() => "agent " + Address + " (RoMON " + RomonId + ", " + ConnectionType + ")";
    }

    /// <summary>The target side of a <see cref="TikRomonConnectionInfo"/>.</summary>
    public sealed class TikRomonTargetInfo
    {
        internal TikRomonTargetInfo(string romonId, string user)
        {
            RomonId = romonId;
            User = user;
        }

        /// <summary>The target's RoMON id — confirmed by the target itself when the connection opened.</summary>
        public string RomonId { get; }

        /// <summary>The user the connection logged in to the target as.</summary>
        public string User { get; }

        /// <inheritdoc/>
        public override string ToString() => "RoMON " + RomonId;
    }

    /// <summary>RoMON details of a connection.</summary>
    public static class TikRomonConnectionExtensions
    {
        /// <summary>
        /// How this connection reached its router through a RoMON agent, or <c>null</c> when it did not — a
        /// direct connection, one not opened yet, or a transport that cannot relay.
        /// </summary>
        /// <param name="connection">The connection to describe.</param>
        public static TikRomonConnectionInfo? GetRomonConnectionInfo(this ITikConnection connection)
        {
            Guard.ArgumentNotNull(connection, nameof(connection));
            return (connection as ITikRomonConnection)?.RomonConnectionInfo;
        }
    }

    /// <summary>Why a connection could not continue from its RoMON agent into the target.</summary>
    public enum TikRomonRelayFailure
    {
        /// <summary>RoMON is switched off on the agent (<c>/tool romon set enabled=yes</c>).</summary>
        RomonNotEnabledOnAgent,

        /// <summary>
        /// The agent could not reach the RoMON id: it is not in the agent's RoMON overlay
        /// (<c>/tool romon discover</c> on the agent lists what it can reach).
        /// </summary>
        TargetUnreachable,

        /// <summary>
        /// The target refused the login. A wrong password and a user whose group lacks the <c>ssh</c> policy
        /// look identical on the wire — RoMON SSH needs that policy even when the target's IP ssh service is
        /// disabled.
        /// </summary>
        TargetRefusedLogin,

        /// <summary>The target accepted the connection but never reached its shell prompt.</summary>
        TargetDidNotRespond,

        /// <summary>
        /// A prompt was reached, but the router behind it does not report the requested RoMON id — the relay
        /// fell back to the agent, or answered from somewhere else.
        /// </summary>
        NotTheTarget,

        /// <summary>The connection to the agent failed while relaying; see the inner exception.</summary>
        TransportFailed,
    }

    /// <summary>
    /// The connection logged in to its RoMON agent but could not continue into the target — see
    /// <see cref="Reason"/>. A failed login to the agent itself is a plain
    /// <see cref="TikConnectionLoginException"/>.
    /// </summary>
    public class TikRomonRelayException : TikConnectionLoginException
    {
        /// <summary>Which step of the relay failed.</summary>
        public TikRomonRelayFailure Reason { get; }

        /// <summary>Creates the exception for <paramref name="reason"/>.</summary>
        /// <param name="reason">Which step of the relay failed.</param>
        /// <param name="message">The complete message.</param>
        /// <param name="innerException">What the connection itself raised, when anything did.</param>
        public TikRomonRelayException(TikRomonRelayFailure reason, string message, Exception? innerException = null)
            : base(message, innerException!)   // System.Exception takes a null inner exception
        {
            Reason = reason;
        }
    }

    /// <summary>
    /// A connection that can continue from a RoMON agent into a target. Internal while the RoMON surface
    /// settles; <see cref="TikConnectionSetup.ApplyTo"/> refuses <see cref="TikConnectionSetup.RomonAgentSetup"/>
    /// on any connection that does not implement it.
    /// </summary>
    internal interface ITikRomonConnection
    {
        /// <summary>The target, set by <see cref="TikConnectionSetup.ApplyTo"/>; <c>null</c> for a direct connection.</summary>
        Cli.RomonSshTarget? RomonTarget { get; set; }

        /// <summary>This transport, as the agent side of <see cref="TikRomonConnectionInfo"/> reports it.</summary>
        TikConnectionType RomonAgentConnectionType { get; }

        /// <summary>Set once the relay has reached the target; <c>null</c> before, and on a direct connection.</summary>
        TikRomonConnectionInfo? RomonConnectionInfo { get; }
    }
}
