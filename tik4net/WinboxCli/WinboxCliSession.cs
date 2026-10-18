using System;
using System.Text;
using tik4net.Cli;
using tik4net.Winbox;

namespace tik4net.WinboxCli
{
    /// <summary>One WinBox CLI session for both WinBox CLI transports, which differ only in the carrier (TCP or MAC layer).</summary>
    internal static class WinboxCliSession
    {
        /// <summary>
        /// A new client over a new carrier, and its login. Through a RoMON agent the carrier goes to the agent and the
        /// relay channel routes it on to the target, whose password opens the terminal there; then
        /// <paramref name="romonEntered"/> gets the agent's RoMON id.
        /// </summary>
        internal static CliConnectionBase.CliTerminalOpen Open(Func<IWinboxM2Channel> newCarrier, RomonRelayTarget? target,
            Encoding encoding, CliClientTimeouts timeouts, string host, int port, string user, string password,
            Action<string> romonEntered)
        {
            var carrier = newCarrier();
            var relay = target == null ? null : new WinboxRomonChannel(carrier, target);
            var client = new WinboxCliClient(relay ?? carrier, encoding, timeouts);
            // A direct login asks for a console without colours (+c), as every terminal login does: RouterOS strips the
            // options before checking the password, and without them it repaints every prompt and echoed keystroke in
            // colour (5.7x the bytes on 6.49.13, Docs/findings-winbox-terminal.md). Through a RoMON agent the login is
            // the agent's, and is left as it is.
            string loginName = target == null ? user + RouterOsCliLogin.TerminalLoginFlags : user;
            return new CliConnectionBase.CliTerminalOpen(client, async ct =>
            {
                await client.LoginAsync(host, port, loginName, password, ct, target?.Password).ConfigureAwait(false);
                if (relay != null)
                    romonEntered(relay.AgentRomonId!);
            });
        }
    }
}
