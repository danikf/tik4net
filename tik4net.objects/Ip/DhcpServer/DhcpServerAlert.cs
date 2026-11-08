using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace tik4net.Objects.Ip.DhcpServer
{
    /// <summary>
    ///  To find any rogue DHCP servers as soon as they appear in your network, DHCP Alert tool can be used. It will monitor the ethernet interface for all DHCP replies and check if this reply comes from a valid DHCP server. If a reply from an unknown DHCP server is detected, alert gets triggered:
    /// 
    /// When the system alerts about a rogue DHCP server, it can execute a custom script.
    /// 
    /// As DHCP replies can be unicast, the 'rogue dhcp detector' may not receive any offer to other dhcp clients at all. To deal with this, the rogue dhcp detector acts as a dhcp client as well - it sends out dhcp discover requests once a minute 
    /// </summary>
    [TikEntity("/ip/dhcp-server/alert")]
    public class DhcpServerAlert
    {
        /// <summary>
        /// .id: primary key of row
        /// </summary>
        [TikProperty(".id", IsReadOnly = true, IsMandatory = true)]
        public string? Id { get; private set; }

        /// <summary>
        /// alert-timeout: Time after which alert will be forgotten. If after that time the same server is detected, new alert will be generated. If set to none timeout will never expire.
        /// </summary>
        [TikProperty("alert-timeout", DefaultValue = "1h", WinboxLabel = "Alert Timeout")]
        public TikField<TikDuration?> AlertTimeout { get; set; }

        /// <summary>
        /// interface: Interface, on which to run rogue DHCP server finder.
        /// </summary>
        [TikProperty("interface", WinboxLabel = "Interface")]
        public TikField<string?> Interface { get; set; }

        /// <summary>
        /// on-alert: Script to run, when an unknown DHCP server is detected.
        /// </summary>
        [TikProperty("on-alert", WinboxLabel = "On Alert")]
        public TikField<string?> OnAlert { get; set; }

        /// <summary>
        /// valid-server: List of MAC addresses of valid DHCP servers.
        /// The router keeps it as a set and prints it sorted, a duplicate once, so a list merge compares it by members.
        /// </summary>
        [TikProperty("valid-server", RouterSortsMembers = true)]
        public TikField<TikValueList<string>?> ValidServer { get; set; }

        /// <summary>
        /// unknown-server: List of MAC addresses of detected unknown DHCP servers. Server is removed from this list after alert-timeout
        /// </summary>
        [TikProperty("unknown-server", IsReadOnly = true)]
        public TikField<string?> UnknownServer { get; private set; }

        /// <summary>
        /// Clears the alert, so the unknown servers it detected are forgotten.
        /// </summary>
        [Obsolete("Use connection.ResetAlert(alert).", false)]
        public void ResetAlert(ITikConnection connection)
        {
            DhcpServerAlertConnectionExtensions.ResetAlert(connection, this);
        }
    }

    /// <summary>
    /// Connection extension class for <see cref="DhcpServerAlert"/>
    /// </summary>
    public static class DhcpServerAlertConnectionExtensions
    {
        /// <summary>
        /// Clears the alert, so the unknown servers it detected are forgotten (<c>/ip/dhcp-server/alert/reset-alert</c>).
        /// </summary>
        /// <param name="connection">Connection to the router.</param>
        /// <param name="alert">A loaded row; its <c>.id</c> addresses the command.</param>
        public static void ResetAlert(this ITikConnection connection, DhcpServerAlert alert)
        {
            Guard.ArgumentNotNull(alert, nameof(alert));
            // Id: IsMandatory=true, so a loaded row always carries it.
            connection.CreateCommandAndParameters("ip/dhcp-server/alert/reset-alert",
                TikSpecialProperties.Id, alert.Id!).ExecuteNonQuery();
        }
    }
}
