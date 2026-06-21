using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace tik4net.Objects.System
{
    /// <summary>
    /// /system/watchdog — hardware and software watchdog configuration (singleton).
    /// The watchdog can reboot the router if a target host becomes unreachable (ping
    /// watchdog) and can automatically generate and optionally e-mail a supout file
    /// after unexpected reboots.
    /// <para>Note: <c>=detail=</c> is rejected by this menu; plain print is used.</para>
    /// <para>See also: https://help.mikrotik.com/docs/display/ROS/Watchdog</para>
    /// </summary>
    // IncludeDetails omitted — detail= is rejected by this singleton.
    [TikEntity("/system/watchdog", IsSingleton = true)]
    public class SystemWatchdog
    {
        /// <summary>watchdog-timer — enables the hardware watchdog timer (reboots when OS hangs). Default: yes.</summary>
        [TikProperty("watchdog-timer", DefaultValue = "yes")]
        public bool WatchdogTimer { get; set; }

        /// <summary>watch-address — IP address to ping; router reboots if this address becomes unreachable for longer than ping-timeout. Set to "none" to disable. Default: none.</summary>
        [TikProperty("watch-address", DefaultValue = "none")]
        public string/*IP*/ WatchAddress { get; set; }

        /// <summary>ping-start-after-boot — delay after boot before the first ping watchdog check begins. Default: 5m.</summary>
        [TikProperty("ping-start-after-boot", DefaultValue = "5m")]
        public string/*time*/ PingStartAfterBoot { get; set; }

        /// <summary>ping-timeout — how long the watch-address must be unreachable before the router reboots. Default: 1m.</summary>
        [TikProperty("ping-timeout", DefaultValue = "1m")]
        public string/*time*/ PingTimeout { get; set; }

        /// <summary>automatic-supout — when yes, a support output file (supout.rif) is automatically created after an unexpected reboot. Default: yes.</summary>
        [TikProperty("automatic-supout", DefaultValue = "yes")]
        public bool AutomaticSupout { get; set; }

        /// <summary>auto-send-supout — when yes, the supout file is automatically e-mailed after an unexpected reboot (requires send-email-* fields). Default: no.</summary>
        [TikProperty("auto-send-supout", DefaultValue = "no")]
        public bool AutoSendSupout { get; set; }

        /// <summary>send-email-from — sender e-mail address used when auto-send-supout=yes.</summary>
        [TikProperty("send-email-from", DefaultValue = "")]
        public string SendEmailFrom { get; set; }

        /// <summary>send-email-to — recipient e-mail address for the auto-sent supout. Comma-separated for multiple recipients.</summary>
        [TikProperty("send-email-to", DefaultValue = "")]
        public string SendEmailTo { get; set; }

        /// <summary>send-smtp-server — SMTP server address used for auto-sending the supout e-mail.</summary>
        [TikProperty("send-smtp-server", DefaultValue = "")]
        public string SendSmtpServer { get; set; }

        /// <summary>Returns a human-readable summary of the watchdog settings.</summary>
        public override string ToString() => string.Format("watchdog: timer={0}, watch-address={1}, auto-supout={2}", WatchdogTimer, WatchAddress, AutomaticSupout);
    }
}
