namespace tik4net.Objects.Interface.Wireless
{
    /// <summary>A legacy (802.11a/b/g) data rate, a member of the wireless and CAPsMAN basic and supported rate lists.</summary>
    /// <remarks>
    /// The router keeps the list as a set and prints it in its own order (<c>11Mbps,1Mbps,2Mbps</c> reads back <c>1Mbps,2Mbps,11Mbps</c>), so a list merge compares it by members.
    /// The 802.11b rates are 1, 2, 5.5 and 11 Mbps; the others are OFDM (802.11a/g).
    /// </remarks>
    public enum WirelessRate
    {
        /// <summary>1Mbps.</summary>
        [TikEnum("1Mbps")] Rate1Mbps,
        /// <summary>2Mbps.</summary>
        [TikEnum("2Mbps")] Rate2Mbps,
        /// <summary>5.5Mbps.</summary>
        [TikEnum("5.5Mbps")] Rate5_5Mbps,
        /// <summary>11Mbps.</summary>
        [TikEnum("11Mbps")] Rate11Mbps,
        /// <summary>6Mbps.</summary>
        [TikEnum("6Mbps")] Rate6Mbps,
        /// <summary>9Mbps.</summary>
        [TikEnum("9Mbps")] Rate9Mbps,
        /// <summary>12Mbps.</summary>
        [TikEnum("12Mbps")] Rate12Mbps,
        /// <summary>18Mbps.</summary>
        [TikEnum("18Mbps")] Rate18Mbps,
        /// <summary>24Mbps.</summary>
        [TikEnum("24Mbps")] Rate24Mbps,
        /// <summary>36Mbps.</summary>
        [TikEnum("36Mbps")] Rate36Mbps,
        /// <summary>48Mbps.</summary>
        [TikEnum("48Mbps")] Rate48Mbps,
        /// <summary>54Mbps.</summary>
        [TikEnum("54Mbps")] Rate54Mbps,
    }
}
