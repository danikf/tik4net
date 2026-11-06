using System;
using System.Text;
using tik4net.Cli;
using tik4net.Winbox;

namespace tik4net.WinboxCli
{
    /// <summary>One WinBox CLI session for both WinBox CLI transports, which differ only in the carrier (TCP or MAC layer).</summary>
    internal static class WinboxCliSession
    {
        /// <summary>The wire-trace channel of the terminal on both carriers: the mepty frames' payloads and notes.</summary>
        internal const string TraceChannel = "wbxcli.mepty";

        /// <summary>
        /// A new client over a new carrier, and its login. Through a RoMON agent the carrier goes to the agent and the
        /// relay channel routes it on to the target, whose password opens the terminal there; then
        /// <paramref name="romonEntered"/> gets the agent's RoMON id. <paramref name="transportName"/> is the leaf's name
        /// ("WinBox CLI", "WinBox CLI MAC"), which the client's messages carry.
        /// </summary>
        internal static CliConnectionBase.CliTerminalOpen Open(string transportName, Func<IWinboxM2Channel> newCarrier,
            RomonRelayTarget? target, Encoding encoding, CliClientTimeouts timeouts, string host, int port, string user,
            string password, Action<string> romonEntered, string? agentMac = null)
        {
            var carrier = newCarrier();
            var relay = target == null ? null : new WinboxRomonChannel(carrier, target, agentMac);
            var client = new WinboxCliClient(relay ?? carrier, encoding, timeouts, transportName, TraceChannel);
            // A direct login asks for a console without colours (+c), as every terminal login does: RouterOS strips the
            // options before checking the password, and without them it repaints every prompt and echoed keystroke in
            // colour (5.7x the bytes on 6.49.13, Docs/findings-winbox-terminal.md). Through a RoMON agent the login is
            // the agent's, and is left as it is.
            string loginName = target == null ? RouterOsCliLogin.WithTerminalLoginFlags(user) : user;
            return new CliConnectionBase.CliTerminalOpen(client.Terminal, async ct =>
            {
                await client.LoginAsync(host, port, loginName, password, ct, target?.Password).ConfigureAwait(false);
                if (relay != null)
                    romonEntered(relay.AgentRomonId!);
            });
        }
    }
}
