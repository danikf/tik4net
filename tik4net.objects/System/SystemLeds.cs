using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace tik4net.Objects.System
{
    /// <summary>
    /// /system/leds — physical LED behaviour configuration on RouterBOARD hardware.
    /// Each entry controls one or more LEDs, associating them with a trigger type
    /// (interface activity, signal strength, modem state, etc.).
    /// Hardware-backed: on Cloud Hosted Routers (CHR) the list is always empty.
    /// <para>See also: https://help.mikrotik.com/docs/display/ROS/LEDs</para>
    /// </summary>
    [TikEntity("/system/leds", IncludeDetails = true)]
    public class SystemLeds
    {
        /// <summary>.id — primary key of the row.</summary>
        [TikProperty(".id", IsReadOnly = true, IsMandatory = true)]
        public string Id { get; private set; }

        /// <summary>
        /// type — LED trigger type; determines what event/state drives the LED.
        /// </summary>
        /// <seealso cref="LedType"/>
        [TikProperty("type", DefaultValue = "off")]
        public LedType Type { get; set; }

        /// <summary>LED trigger type for <see cref="Type"/>.</summary>
        public enum LedType
        {
            /// <summary>off — LED is permanently off.</summary>
            [TikEnum("off")] Off,
            /// <summary>on — LED is permanently on.</summary>
            [TikEnum("on")] On,
            /// <summary>ap-cap — LED indicates CAPsMAN/CAP association state.</summary>
            [TikEnum("ap-cap")] ApCap,
            /// <summary>fan-fault — LED lights on fan fault.</summary>
            [TikEnum("fan-fault")] FanFault,
            /// <summary>flash-access — LED blinks on flash/storage access.</summary>
            [TikEnum("flash-access")] FlashAccess,
            /// <summary>interface-activity — LED blinks on any traffic (TX+RX) on the interface.</summary>
            [TikEnum("interface-activity")] InterfaceActivity,
            /// <summary>interface-receive — LED blinks on received (RX) traffic on the interface.</summary>
            [TikEnum("interface-receive")] InterfaceReceive,
            /// <summary>interface-transmit — LED blinks on transmitted (TX) traffic on the interface.</summary>
            [TikEnum("interface-transmit")] InterfaceTransmit,
            /// <summary>interface-status — LED reflects link/connected state of the interface.</summary>
            [TikEnum("interface-status")] InterfaceStatus,
            /// <summary>interface-speed — LED reflects the negotiated speed of the interface.</summary>
            [TikEnum("interface-speed")] InterfaceSpeed,
            /// <summary>interface-speed-1G — LED lights when the interface is running at 1 Gbps.</summary>
            [TikEnum("interface-speed-1G")] InterfaceSpeed1G,
            /// <summary>interface-speed-25G — LED lights when the interface is running at 25 Gbps.</summary>
            [TikEnum("interface-speed-25G")] InterfaceSpeed25G,
            /// <summary>interface-speed-100G — LED lights when the interface is running at 100 Gbps.</summary>
            [TikEnum("interface-speed-100G")] InterfaceSpeed100G,
            /// <summary>modem-signal — LED reflects modem signal strength.</summary>
            [TikEnum("modem-signal")] ModemSignal,
            /// <summary>modem-technology — LED reflects modem radio technology (2G/3G/4G/5G).</summary>
            [TikEnum("modem-technology")] ModemTechnology,
            /// <summary>poe-fault — LED lights on PoE output fault.</summary>
            [TikEnum("poe-fault")] PoeFault,
            /// <summary>poe-out — LED reflects PoE output state.</summary>
            [TikEnum("poe-out")] PoeOut,
            /// <summary>wireless-signal-strength — LED reflects wireless signal strength.</summary>
            [TikEnum("wireless-signal-strength")] WirelessSignalStrength,
            /// <summary>wireless-status — LED reflects wireless association status.</summary>
            [TikEnum("wireless-status")] WirelessStatus,
        }

        /// <summary>leds — hardware LED identifier(s) controlled by this entry (hardware-specific names, e.g. "user-led").</summary>
        [TikProperty("leds")]
        public string Leds { get; set; }

        /// <summary>interface — name of the interface whose state/traffic drives the LED (used with interface-* and wireless-* types).</summary>
        [TikProperty("interface")]
        public string Interface { get; set; }

        /// <summary>modem-signal-threshold — RSSI threshold (dBm) for the modem-signal LED type; LED is on when signal is above this value. Real default: -70; 0 is the CLR sentinel (omitted on add).</summary>
        // Range e.g. -120..0; DefaultValue="0" so CLR default 0 is omitted on add.
        [TikProperty("modem-signal-threshold", DefaultValue = "0")]
        public int ModemSignalThreshold { get; set; }

        /// <summary>disabled — when true this LED entry is disabled. Default: no.</summary>
        [TikProperty("disabled", DefaultValue = "no")]
        public bool Disabled { get; set; }

        /// <summary>Returns a human-readable summary of this LED entry.</summary>
        public override string ToString() => string.Format("leds: {0} type={1} iface={2}", Leds, Type, Interface);
    }
}
