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
    [TikEntity("ip/dhcp-server/alert")]
    public class DhcpServerAlert
    {
        /// <summary>
        /// .id: primary key of row
        /// </summary>
        [TikProperty(".id", IsReadOnly = true, IsMandatory = true)]
        public string Id { get; private set; }

        /// <summary>
        /// alert-timeout: Time after which alert will be forgotten. If after that time the same server is detected, new alert will be generated. If set to none timeout will never expire.
        /// </summary>
        [TikProperty("alert-timeout", DefaultValue = "none")]
        public string/*none | time*/ AlertTimeout { get; set; }

        /// <summary>
        /// interface: Interface, on which to run rogue DHCP server finder.
        /// </summary>
        [TikProperty("interface")]
        public string Interface { get; set; }

        /// <summary>
        /// on-alert: Script to run, when an unknown DHCP server is detected.
        /// </summary>
        [TikProperty("on-alert")]
        public string OnAlert { get; set; }

        /// <summary>
        /// valid-server: List of MAC addresses of valid DHCP servers.
        /// </summary>
        [TikProperty("valid-server")]
        public string ValidServer { get; set; }

        /// <summary>
        /// unknown-server: List of MAC addresses of detected unknown DHCP servers. Server is removed from this list after alert-timeout
        /// </summary>
        [TikProperty("unknown-server", IsReadOnly = true)]
        public string UnknownServer { get; private set; }

        /// <summary>
        /// Convert a dynamic lease to a static one
        /// </summary>
        public void ResetAlert(ITikConnection connection)
        {
            connection.CreateCommandAndParameters("ip/dhcp-server/alert/reset-alert",
                TikSpecialProperties.Id, Id).ExecuteNonQuery();
        }
    }
}
