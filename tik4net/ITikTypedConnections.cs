using tik4net.Cli;

namespace tik4net
{
    /// <summary>
    /// A binary API connection (<see cref="TikConnectionType.Api"/> / <see cref="TikConnectionType.ApiSsl"/>)
    /// — everything that transport can do, in one type.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This and its siblings exist so that <b>what a transport can do is answered by the compiler</b>. The
    /// capability model is still there and still fail-closed, but a caller who names the transport when
    /// writing the code should not have to ask at runtime, or cast, or discover by exception. Return one of
    /// these from the transport's own factory (<c>setup.CreateApiConnection()</c>) and Safe Mode, tagging,
    /// TLS and the raw sentence dialect are simply members — IntelliSense lists them, and a transport that
    /// lacks one does not compile rather than throwing.
    /// </para>
    /// <para>
    /// <b>The type says what the transport implements; <see cref="TikConnectionCapabilityExtensions.Supports"/>
    /// says what this router allows.</b> The two are not the same question, and the second one does not go
    /// away: Safe Mode over native WinBox needs RouterOS 7.18+, and REST needs 7.1+ before anything works at
    /// all. Keep checking <c>Supports</c> when the answer depends on the box you are talking to; the type
    /// covers only what is decided by which transport you chose.
    /// </para>
    /// <para>
    /// <see cref="TikConnectionSetup.Create(TikConnectionType)"/> still returns a plain
    /// <see cref="ITikConnection"/>, and correctly so — when the transport comes from a config file there is
    /// no static type to hand back. Pattern-match to reach a facet there:
    /// <c>if (conn is ITikSafeModeConnection safe) …</c>.
    /// </para>
    /// </remarks>
    public interface ITikApiConnection : ITikConnection, ITikConnectionCapabilities,
        ITikRawSentenceConnection, ITikSafeModeConnection, ITikTaggedConnection, ITikTlsConnection
    {
    }

    /// <summary>
    /// A REST connection (<see cref="TikConnectionType.Rest"/> / <see cref="TikConnectionType.RestSsl"/>).
    /// </summary>
    /// <remarks>
    /// Deliberately the thinnest of these types, and the thinness is the information: REST is stateless, so
    /// there is no session to bind Safe Mode to, and it has a request shape rather than a command language,
    /// so neither raw level is offered. Those members are absent here rather than present and throwing.
    /// </remarks>
    public interface ITikRestConnection : ITikConnection, ITikConnectionCapabilities, ITikTlsConnection
    {
    }

    /// <summary>
    /// A RouterOS CLI connection over a terminal — Telnet, SSH, or the WinBox terminal
    /// (<see cref="TikConnectionType.Telnet"/>, <see cref="TikConnectionType.Ssh"/>,
    /// <see cref="TikConnectionType.WinboxCli"/>).
    /// </summary>
    /// <remarks>
    /// Carries two things the other transports have no equivalent of: <see cref="ITikCliCompletion"/>, the
    /// router's own Tab-completion, which was previously reachable only by casting and had no convenience
    /// shim at all; and <see cref="ITikCancellationModeConnection"/>, because a terminal is the only place
    /// where cancelling mid-command is a choice between waiting and losing the session.
    /// </remarks>
    public interface ITikCliConnection : ITikConnection, ITikConnectionCapabilities,
        ITikRawSentenceConnection, ITikSafeModeConnection, ITikCancellationModeConnection, ITikCliCompletion
    {
    }

    /// <summary>
    /// A RouterOS CLI connection carried over the MAC layer — MAC-Telnet or WinBox CLI over MAC
    /// (<see cref="TikConnectionType.MacTelnet"/>, <see cref="TikConnectionType.WinboxCliMac"/>).
    /// </summary>
    /// <remarks>
    /// Everything <see cref="ITikCliConnection"/> has, plus the router MAC — these reach a router with no IP
    /// route, or no IP address at all.
    /// </remarks>
    public interface ITikMacCliConnection : ITikCliConnection, ITikMacLayerConnection
    {
    }

    /// <summary>
    /// A structured WinBox M2 connection (<see cref="TikConnectionType.WinboxNative"/>).
    /// </summary>
    /// <remarks>
    /// No raw level: M2 addresses windows and fields numerically, so there is no command language for a
    /// caller to write. Safe Mode is here because the M2 session can hold it (RouterOS 7.18+ — the version
    /// part is what <c>Supports</c> is still for).
    /// </remarks>
    public interface ITikWinboxNativeConnection : ITikConnection, ITikConnectionCapabilities,
        ITikSafeModeConnection
    {
    }

    /// <summary>
    /// A structured WinBox M2 connection carried over the MAC layer
    /// (<see cref="TikConnectionType.WinboxNativeMac"/>).
    /// </summary>
    public interface ITikWinboxNativeMacConnection : ITikWinboxNativeConnection, ITikMacLayerConnection
    {
    }
}
