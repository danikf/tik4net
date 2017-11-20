
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace InvertedTomato.TikLink.Records {
    /// <summary>
    /// /interface wireless registration-table: In the registration table you can see various information about currently connected clients. It is used only for Access Points. All properties are read-only.
    /// </summary>
    [RosRecord("/interface/wireless/registration-table")] // Read-only
    public class InterfaceWirelessRegistrationTable : SetRecordBase {
        /// <summary>
        /// Unknown: whether the data exchange is allowed with the peer (i.e., whether 802.1x authentication is completed, if needed)
        /// </summary>
        [RosProperty("802.1x-port-enabled")] // Read-only
        public string /*802.1x-port-enabled (yes | no)*/ Port8021Enabled { get; private set; }

        /// <summary>
        /// ack-timeout: current value of ack-timeout
        /// </summary>
        [RosProperty("ack-timeout")] // Read-only
        public int AckTimeout { get; private set; }

        /// <summary>
        /// ap: Shows whether registered device is configured as access point.
        /// </summary>
        [RosProperty("ap")] // Read-only
        public bool Ap { get; private set; }

        /// <summary>
        /// ap-tx-limit: transmit rate limit on the AP, in bits per second
        /// </summary>
        [RosProperty("ap-tx-limit")] // Read-only
        public int ApTxLimit { get; private set; }

        /// <summary>
        /// authentication-type: authentication method used for the peer
        /// </summary>
        [RosProperty("authentication-type")] // Read-only
        public string AuthenticationType { get; private set; }

        /// <summary>
        /// bridge: 
        /// </summary>
        [RosProperty("bridge")] // Read-only
        public bool Bridge { get; private set; }

        /// <summary>
        /// bytes: number of sent and received packet bytes
        /// </summary>
        [RosProperty("bytes")] // Read-only
        public string Bytes { get; private set; }

        /// <summary>
        /// client-tx-limit: transmit rate limit on the AP, in bits per second
        /// </summary>
        [RosProperty("client-tx-limit")] // Read-only
        public int ClientTxLimit { get; private set; }

        /// <summary>
        /// comment: Description of an entry. comment is taken from appropriate  Access List entry if specified.
        /// </summary>
        [RosProperty("comment")] // Read-only
        public string Comment { get; private set; }

        /// <summary>
        /// compression: whether data compresson is used for this peer
        /// </summary>
        [RosProperty("compression")] // Read-only
        public bool Compression { get; private set; }

        /// <summary>
        /// distance: 
        /// </summary>
        [RosProperty("distance")] // Read-only
        public int Distance { get; private set; }

        /// <summary>
        /// encryption: unicast encryption algorithm used
        /// </summary>
        [RosProperty("encryption")] // Read-only
        public string Encryption { get; private set; }

        /// <summary>
        /// evm-ch0: 
        /// </summary>
        [RosProperty("evm-ch0")] // Read-only
        public string EvmCh0 { get; private set; }

        /// <summary>
        /// evm-ch1: 
        /// </summary>
        [RosProperty("evm-ch1")] // Read-only
        public string EvmCh1 { get; private set; }

        /// <summary>
        /// evm-ch2: 
        /// </summary>
        [RosProperty("evm-ch2")] // Read-only
        public string EvmCh2 { get; private set; }

        /// <summary>
        /// frame-bytes: number of sent and received data bytes excluding header information
        /// </summary>
        [RosProperty("frame-bytes")] // Read-only
        public string FrameBytes { get; private set; }

        /// <summary>
        /// frames: Number of frames that need to be sent over wireless link. This value can be compared to  hw-frames to check wireless retransmits.   Read more &gt;&gt;
        /// </summary>
        [RosProperty("frames")] // Read-only
        public string Frames { get; private set; }

        /// <summary>
        /// framing-current-size: current size of combined frames
        /// </summary>
        [RosProperty("framing-current-size")] // Read-only
        public int FramingCurrentSize { get; private set; }

        /// <summary>
        /// framing-limit: maximal size of combined frames
        /// </summary>
        [RosProperty("framing-limit")] // Read-only
        public int FramingLimit { get; private set; }

        /// <summary>
        /// framing-mode: the method how to combine frames
        /// </summary>
        [RosProperty("framing-mode")] // Read-only
        public string FramingMode { get; private set; }

        /// <summary>
        /// group-encryption: group encryption algorithm used
        /// </summary>
        [RosProperty("group-encryption")] // Read-only
        public string GroupEncryption { get; private set; }

        /// <summary>
        /// hw-frame-bytes: number of sent and received data bytes including header information
        /// </summary>
        [RosProperty("hw-frame-bytes")] // Read-only
        public string HwFrameBytes { get; private set; }

        /// <summary>
        /// hw-frames: Number of frames sent over wireless link by the driver. Tihs value can be compared to  frames to check wireless retransmits.  Read more &gt;&gt;
        /// </summary>
        [RosProperty("hw-frames")] // Read-only
        public string HwFrames { get; private set; }

        /// <summary>
        /// interface: Name of the wireless interface to which wireless client is associated
        /// </summary>
        [RosProperty("interface")] // Read-only
        public string Interface { get; private set; }

        /// <summary>
        /// last-activity: last interface data tx/rx activity
        /// </summary>
        [RosProperty("last-activity")] // Read-only
        public string LastActivity { get; private set; }

        /// <summary>
        /// last-ip: IP address found in the last IP packet received from the registered client
        /// </summary>
        [RosProperty("last-ip")] // Read-only
        public string LastIp { get; private set; }

        /// <summary>
        /// mac-address: MAC address of the registered client
        /// </summary>
        [RosProperty("mac-address")] // Read-only
        public string MacAddress { get; private set; }

        /// <summary>
        /// management-protection: 
        /// </summary>
        [RosProperty("management-protection")] // Read-only
        public bool ManagementProtection { get; private set; }

        /// <summary>
        /// nstreme: Shows whether nstreme is enabled
        /// </summary>
        [RosProperty("nstreme")] // Read-only
        public bool Nstreme { get; private set; }

        /// <summary>
        /// p-throughput: estimated approximate throughput that is expected to the given peer, taking into account the effective transmit rate and hardware retries. Calculated once in 5 seconds
        /// </summary>
        [RosProperty("p-throughput")] // Read-only
        public int PThroughput { get; private set; }

        /// <summary>
        /// packed-bytes: number of bytes packed into larger frames for transmitting/receiving (framing)
        /// </summary>
        [RosProperty("packed-bytes")] // Read-only
        public string PackedBytes { get; private set; }

        /// <summary>
        /// packed-frames: number of frames packed into larger ones for transmitting/receiving (framing)
        /// </summary>
        [RosProperty("packed-frames")] // Read-only
        public string PackedFrames { get; private set; }

        /// <summary>
        /// packets: number of sent and received Option layer packets
        /// </summary>
        [RosProperty("packets")] // Read-only
        public string Packets { get; private set; }

        /// <summary>
        /// radio-name: radio name of the peer
        /// </summary>
        [RosProperty("radio-name")] // Read-only
        public string RadioName { get; private set; }

        /// <summary>
        /// routeros-version: RouterOS version of the registered client
        /// </summary>
        [RosProperty("routeros-version")] // Read-only
        public string RouterosVersion { get; private set; }

        /// <summary>
        /// rx-ccq: Client Connection Quality (CCQ) for receive.  Read more &gt;&gt; 
        /// </summary>
        [RosProperty("rx-ccq")] // Read-only
        public string RxCcq { get; private set; }

        /// <summary>
        /// rx-rate: receive data rate
        /// </summary>
        [RosProperty("rx-rate")] // Read-only
        public string RxRate { get; private set; }

        /// <summary>
        /// signal-strength: average strength of the client signal recevied by the AP
        /// </summary>
        [RosProperty("signal-strength")] // Read-only
        public string SignalStrength { get; private set; }

        /// <summary>
        /// signal-strength-ch0: 
        /// </summary>
        [RosProperty("signal-strength-ch0")] // Read-only
        public string SignalStrengthCh0 { get; private set; }

        /// <summary>
        /// signal-strength-ch1: 
        /// </summary>
        [RosProperty("signal-strength-ch1")] // Read-only
        public string SignalStrengthCh1 { get; private set; }

        /// <summary>
        /// signal-strength-ch2: 
        /// </summary>
        [RosProperty("signal-strength-ch2")] // Read-only
        public string SignalStrengthCh2 { get; private set; }

        /// <summary>
        /// signal-to-noise: 
        /// </summary>
        [RosProperty("signal-to-noise")] // Read-only
        public string SignalToNoise { get; private set; }

        /// <summary>
        /// strength-at-rates: signal strength level at different rates together with time how long were these rates used
        /// </summary>
        [RosProperty("strength-at-rates")] // Read-only
        public string StrengthAtRates { get; private set; }

        /// <summary>
        /// tdma-retx: 
        /// </summary>
        [RosProperty("tdma-retx")] // Read-only
        public string TdmaRetx { get; private set; }

        /// <summary>
        /// tdma-rx-size: 
        /// </summary>
        [RosProperty("tdma-rx-size")] // Read-only
        public string TdmaRxSize { get; private set; }

        /// <summary>
        /// tdma-timing-offset
        /// tdma-timing-offset is proportional to distance and is approximately two times the propagation delay.
        /// AP measures this so that it can tell clients what offset to use for their transmissions - clients then subtract this offset from their target transmission time such that propagation delay is accounted for and transmission arrives at AP when expected. You may occasionally see small negative value (like few usecs) there for close range clients because of additional unaccounted delay that may be produced in transmitter or receiver hardware that varies from chipset to chipset.
        /// </summary>
        [RosProperty("tdma-timing-offset")] // Read-only
        public string TdmaTimingOffset { get; private set; }

        /// <summary>
        /// tdma-tx-size: Value in bytes that specifies the size of data unit whose loss can be detected (data unit over which CRC is calculated) sent by device. In general - the bigger the better, because overhead is less. On the other hand, small value in this setting can not always be considered a signal that connection is poor - if device does not have enough pending data that would enable it to use bigger data units (e.g. if you are just pinging over link), this value will not go up.
        /// </summary>
        [RosProperty("tdma-tx-size")] // Read-only
        public int TdmaTxSize { get; private set; }

        /// <summary>
        /// tdma-windfull: 
        /// </summary>
        [RosProperty("tdma-windfull")] // Read-only
        public string TdmaWindfull { get; private set; }

        /// <summary>
        /// tx-ccq: Client Connection Quality (CCQ) for transmit.  Read more &gt;&gt; 
        /// </summary>
        [RosProperty("tx-ccq")] // Read-only
        public string TxCcq { get; private set; }

        /// <summary>
        /// tx-evm-ch0: 
        /// </summary>
        [RosProperty("tx-evm-ch0")] // Read-only
        public string TxEvmCh0 { get; private set; }

        /// <summary>
        /// tx-evm-ch1: 
        /// </summary>
        [RosProperty("tx-evm-ch1")] // Read-only
        public string TxEvmCh1 { get; private set; }

        /// <summary>
        /// tx-evm-ch2: 
        /// </summary>
        [RosProperty("tx-evm-ch2")] // Read-only
        public string TxEvmCh2 { get; private set; }

        /// <summary>
        /// tx-frames-timed-out: 
        /// </summary>
        [RosProperty("tx-frames-timed-out")] // Read-only
        public string TxFramesTimedOut { get; private set; }

        /// <summary>
        /// tx-rate: 
        /// </summary>
        [RosProperty("tx-rate")] // Read-only
        public string TxRate { get; private set; }

        /// <summary>
        /// tx-signal-strength: 
        /// </summary>
        [RosProperty("tx-signal-strength")] // Read-only
        public string TxSignalStrength { get; private set; }

        /// <summary>
        /// tx-signal-strength-ch0: 
        /// </summary>
        [RosProperty("tx-signal-strength-ch0")] // Read-only
        public string TxSignalStrengthCh0 { get; private set; }

        /// <summary>
        /// tx-signal-strength-ch1: 
        /// </summary>
        [RosProperty("tx-signal-strength-ch1")] // Read-only
        public string TxSignalStrengthCh1 { get; private set; }

        /// <summary>
        /// tx-signal-strength-ch2: 
        /// </summary>
        [RosProperty("tx-signal-strength-ch2")] // Read-only
        public string TxSignalStrengthCh2 { get; private set; }

        /// <summary>
        /// uptime: time the client is associated with the access point
        /// </summary>
        [RosProperty("uptime")] // Read-only
        public TimeSpan Uptime { get; private set; }

        /// <summary>
        /// wds: whether the connected client is using wds or not
        /// </summary>
        [RosProperty("wds")] // Read-only
        public bool Wds { get; private set; }

        /// <summary>
        /// wmm-enabled: Shows whether  WMM is enabled.
        /// </summary>
        [RosProperty("wmm-enabled")] // Read-only
        public bool WmmEnabled { get; private set; }
    }
}
