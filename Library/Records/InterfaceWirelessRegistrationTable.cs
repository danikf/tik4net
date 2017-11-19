﻿
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace InvertedTomato.TikLink.Records {
    /// <summary>
    /// /interface wireless registration-table: In the registration table you can see various information about currently connected clients. It is used only for Access Points. All properties are read-only.
    /// </summary>
    [RosRecord("/interface/wireless/registration-table", IsReadOnly = true)]
    public class InterfaceWirelessRegistrationTable : SetRecordBase {
        /// <summary>
        /// Unknown: whether the data exchange is allowed with the peer (i.e., whether 802.1x authentication is completed, if needed)
        /// </summary>
        [RosProperty("802.1x-port-enabled", IsReadOnly = true)]
        public string /*802.1x-port-enabled (yes | no)*/ Port8021Enabled { get; private set; }

        /// <summary>
        /// ack-timeout: current value of ack-timeout
        /// </summary>
        [RosProperty("ack-timeout", IsReadOnly = true)]
        public int AckTimeout { get; private set; }

        /// <summary>
        /// ap: Shows whether registered device is configured as access point.
        /// </summary>
        [RosProperty("ap", IsReadOnly = true)]
        public bool Ap { get; private set; }

        /// <summary>
        /// ap-tx-limit: transmit rate limit on the AP, in bits per second
        /// </summary>
        [RosProperty("ap-tx-limit", IsReadOnly = true)]
        public int ApTxLimit { get; private set; }

        /// <summary>
        /// authentication-type: authentication method used for the peer
        /// </summary>
        [RosProperty("authentication-type", IsReadOnly = true)]
        public string AuthenticationType { get; private set; }

        /// <summary>
        /// bridge: 
        /// </summary>
        [RosProperty("bridge", IsReadOnly = true)]
        public bool Bridge { get; private set; }

        /// <summary>
        /// bytes: number of sent and received packet bytes
        /// </summary>
        [RosProperty("bytes", IsReadOnly = true)]
        public string Bytes { get; private set; }

        /// <summary>
        /// client-tx-limit: transmit rate limit on the AP, in bits per second
        /// </summary>
        [RosProperty("client-tx-limit", IsReadOnly = true)]
        public int ClientTxLimit { get; private set; }

        /// <summary>
        /// comment: Description of an entry. comment is taken from appropriate  Access List entry if specified.
        /// </summary>
        [RosProperty("comment", IsReadOnly = true)]
        public string Comment { get; private set; }

        /// <summary>
        /// compression: whether data compresson is used for this peer
        /// </summary>
        [RosProperty("compression", IsReadOnly = true)]
        public bool Compression { get; private set; }

        /// <summary>
        /// distance: 
        /// </summary>
        [RosProperty("distance", IsReadOnly = true)]
        public int Distance { get; private set; }

        /// <summary>
        /// encryption: unicast encryption algorithm used
        /// </summary>
        [RosProperty("encryption", IsReadOnly = true)]
        public string Encryption { get; private set; }

        /// <summary>
        /// evm-ch0: 
        /// </summary>
        [RosProperty("evm-ch0", IsReadOnly = true)]
        public string EvmCh0 { get; private set; }

        /// <summary>
        /// evm-ch1: 
        /// </summary>
        [RosProperty("evm-ch1", IsReadOnly = true)]
        public string EvmCh1 { get; private set; }

        /// <summary>
        /// evm-ch2: 
        /// </summary>
        [RosProperty("evm-ch2", IsReadOnly = true)]
        public string EvmCh2 { get; private set; }

        /// <summary>
        /// frame-bytes: number of sent and received data bytes excluding header information
        /// </summary>
        [RosProperty("frame-bytes", IsReadOnly = true)]
        public string FrameBytes { get; private set; }

        /// <summary>
        /// frames: Number of frames that need to be sent over wireless link. This value can be compared to  hw-frames to check wireless retransmits.   Read more &gt;&gt;
        /// </summary>
        [RosProperty("frames", IsReadOnly = true)]
        public string Frames { get; private set; }

        /// <summary>
        /// framing-current-size: current size of combined frames
        /// </summary>
        [RosProperty("framing-current-size", IsReadOnly = true)]
        public int FramingCurrentSize { get; private set; }

        /// <summary>
        /// framing-limit: maximal size of combined frames
        /// </summary>
        [RosProperty("framing-limit", IsReadOnly = true)]
        public int FramingLimit { get; private set; }

        /// <summary>
        /// framing-mode: the method how to combine frames
        /// </summary>
        [RosProperty("framing-mode", IsReadOnly = true)]
        public string FramingMode { get; private set; }

        /// <summary>
        /// group-encryption: group encryption algorithm used
        /// </summary>
        [RosProperty("group-encryption", IsReadOnly = true)]
        public string GroupEncryption { get; private set; }

        /// <summary>
        /// hw-frame-bytes: number of sent and received data bytes including header information
        /// </summary>
        [RosProperty("hw-frame-bytes", IsReadOnly = true)]
        public string HwFrameBytes { get; private set; }

        /// <summary>
        /// hw-frames: Number of frames sent over wireless link by the driver. Tihs value can be compared to  frames to check wireless retransmits.  Read more &gt;&gt;
        /// </summary>
        [RosProperty("hw-frames", IsReadOnly = true)]
        public string HwFrames { get; private set; }

        /// <summary>
        /// interface: Name of the wireless interface to which wireless client is associated
        /// </summary>
        [RosProperty("interface", IsReadOnly = true)]
        public string Interface { get; private set; }

        /// <summary>
        /// last-activity: last interface data tx/rx activity
        /// </summary>
        [RosProperty("last-activity", IsReadOnly = true)]
        public string LastActivity { get; private set; }

        /// <summary>
        /// last-ip: IP address found in the last IP packet received from the registered client
        /// </summary>
        [RosProperty("last-ip", IsReadOnly = true)]
        public string LastIp { get; private set; }

        /// <summary>
        /// mac-address: MAC address of the registered client
        /// </summary>
        [RosProperty("mac-address", IsReadOnly = true)]
        public string MacAddress { get; private set; }

        /// <summary>
        /// management-protection: 
        /// </summary>
        [RosProperty("management-protection", IsReadOnly = true)]
        public bool ManagementProtection { get; private set; }

        /// <summary>
        /// nstreme: Shows whether nstreme is enabled
        /// </summary>
        [RosProperty("nstreme", IsReadOnly = true)]
        public bool Nstreme { get; private set; }

        /// <summary>
        /// p-throughput: estimated approximate throughput that is expected to the given peer, taking into account the effective transmit rate and hardware retries. Calculated once in 5 seconds
        /// </summary>
        [RosProperty("p-throughput", IsReadOnly = true)]
        public int PThroughput { get; private set; }

        /// <summary>
        /// packed-bytes: number of bytes packed into larger frames for transmitting/receiving (framing)
        /// </summary>
        [RosProperty("packed-bytes", IsReadOnly = true)]
        public string PackedBytes { get; private set; }

        /// <summary>
        /// packed-frames: number of frames packed into larger ones for transmitting/receiving (framing)
        /// </summary>
        [RosProperty("packed-frames", IsReadOnly = true)]
        public string PackedFrames { get; private set; }

        /// <summary>
        /// packets: number of sent and received Option layer packets
        /// </summary>
        [RosProperty("packets", IsReadOnly = true)]
        public string Packets { get; private set; }

        /// <summary>
        /// radio-name: radio name of the peer
        /// </summary>
        [RosProperty("radio-name", IsReadOnly = true)]
        public string RadioName { get; private set; }

        /// <summary>
        /// routeros-version: RouterOS version of the registered client
        /// </summary>
        [RosProperty("routeros-version", IsReadOnly = true)]
        public string RouterosVersion { get; private set; }

        /// <summary>
        /// rx-ccq: Client Connection Quality (CCQ) for receive.  Read more &gt;&gt; 
        /// </summary>
        [RosProperty("rx-ccq", IsReadOnly = true)]
        public string RxCcq { get; private set; }

        /// <summary>
        /// rx-rate: receive data rate
        /// </summary>
        [RosProperty("rx-rate", IsReadOnly = true)]
        public string RxRate { get; private set; }

        /// <summary>
        /// signal-strength: average strength of the client signal recevied by the AP
        /// </summary>
        [RosProperty("signal-strength", IsReadOnly = true)]
        public string SignalStrength { get; private set; }

        /// <summary>
        /// signal-strength-ch0: 
        /// </summary>
        [RosProperty("signal-strength-ch0", IsReadOnly = true)]
        public string SignalStrengthCh0 { get; private set; }

        /// <summary>
        /// signal-strength-ch1: 
        /// </summary>
        [RosProperty("signal-strength-ch1", IsReadOnly = true)]
        public string SignalStrengthCh1 { get; private set; }

        /// <summary>
        /// signal-strength-ch2: 
        /// </summary>
        [RosProperty("signal-strength-ch2", IsReadOnly = true)]
        public string SignalStrengthCh2 { get; private set; }

        /// <summary>
        /// signal-to-noise: 
        /// </summary>
        [RosProperty("signal-to-noise", IsReadOnly = true)]
        public string SignalToNoise { get; private set; }

        /// <summary>
        /// strength-at-rates: signal strength level at different rates together with time how long were these rates used
        /// </summary>
        [RosProperty("strength-at-rates", IsReadOnly = true)]
        public string StrengthAtRates { get; private set; }

        /// <summary>
        /// tdma-retx: 
        /// </summary>
        [RosProperty("tdma-retx", IsReadOnly = true)]
        public string TdmaRetx { get; private set; }

        /// <summary>
        /// tdma-rx-size: 
        /// </summary>
        [RosProperty("tdma-rx-size", IsReadOnly = true)]
        public string TdmaRxSize { get; private set; }

        /// <summary>
        /// tdma-timing-offset
        /// tdma-timing-offset is proportional to distance and is approximately two times the propagation delay.
        /// AP measures this so that it can tell clients what offset to use for their transmissions - clients then subtract this offset from their target transmission time such that propagation delay is accounted for and transmission arrives at AP when expected. You may occasionally see small negative value (like few usecs) there for close range clients because of additional unaccounted delay that may be produced in transmitter or receiver hardware that varies from chipset to chipset.
        /// </summary>
        [RosProperty("tdma-timing-offset", IsReadOnly = true)]
        public string TdmaTimingOffset { get; private set; }

        /// <summary>
        /// tdma-tx-size: Value in bytes that specifies the size of data unit whose loss can be detected (data unit over which CRC is calculated) sent by device. In general - the bigger the better, because overhead is less. On the other hand, small value in this setting can not always be considered a signal that connection is poor - if device does not have enough pending data that would enable it to use bigger data units (e.g. if you are just pinging over link), this value will not go up.
        /// </summary>
        [RosProperty("tdma-tx-size", IsReadOnly = true)]
        public int TdmaTxSize { get; private set; }

        /// <summary>
        /// tdma-windfull: 
        /// </summary>
        [RosProperty("tdma-windfull", IsReadOnly = true)]
        public string TdmaWindfull { get; private set; }

        /// <summary>
        /// tx-ccq: Client Connection Quality (CCQ) for transmit.  Read more &gt;&gt; 
        /// </summary>
        [RosProperty("tx-ccq", IsReadOnly = true)]
        public string TxCcq { get; private set; }

        /// <summary>
        /// tx-evm-ch0: 
        /// </summary>
        [RosProperty("tx-evm-ch0", IsReadOnly = true)]
        public string TxEvmCh0 { get; private set; }

        /// <summary>
        /// tx-evm-ch1: 
        /// </summary>
        [RosProperty("tx-evm-ch1", IsReadOnly = true)]
        public string TxEvmCh1 { get; private set; }

        /// <summary>
        /// tx-evm-ch2: 
        /// </summary>
        [RosProperty("tx-evm-ch2", IsReadOnly = true)]
        public string TxEvmCh2 { get; private set; }

        /// <summary>
        /// tx-frames-timed-out: 
        /// </summary>
        [RosProperty("tx-frames-timed-out", IsReadOnly = true)]
        public string TxFramesTimedOut { get; private set; }

        /// <summary>
        /// tx-rate: 
        /// </summary>
        [RosProperty("tx-rate", IsReadOnly = true)]
        public string TxRate { get; private set; }

        /// <summary>
        /// tx-signal-strength: 
        /// </summary>
        [RosProperty("tx-signal-strength", IsReadOnly = true)]
        public string TxSignalStrength { get; private set; }

        /// <summary>
        /// tx-signal-strength-ch0: 
        /// </summary>
        [RosProperty("tx-signal-strength-ch0", IsReadOnly = true)]
        public string TxSignalStrengthCh0 { get; private set; }

        /// <summary>
        /// tx-signal-strength-ch1: 
        /// </summary>
        [RosProperty("tx-signal-strength-ch1", IsReadOnly = true)]
        public string TxSignalStrengthCh1 { get; private set; }

        /// <summary>
        /// tx-signal-strength-ch2: 
        /// </summary>
        [RosProperty("tx-signal-strength-ch2", IsReadOnly = true)]
        public string TxSignalStrengthCh2 { get; private set; }

        /// <summary>
        /// uptime: time the client is associated with the access point
        /// </summary>
        [RosProperty("uptime", IsReadOnly = true)]
        public TimeSpan Uptime { get; private set; }

        /// <summary>
        /// wds: whether the connected client is using wds or not
        /// </summary>
        [RosProperty("wds", IsReadOnly = true)]
        public bool Wds { get; private set; }

        /// <summary>
        /// wmm-enabled: Shows whether  WMM is enabled.
        /// </summary>
        [RosProperty("wmm-enabled", IsReadOnly = true)]
        public bool WmmEnabled { get; private set; }
    }
}