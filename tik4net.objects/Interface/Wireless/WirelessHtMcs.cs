namespace tik4net.Objects.Interface.Wireless
{
    /// <summary>An 802.11n modulation and coding scheme, a member of the wireless and CAPsMAN HT basic and supported MCS lists.</summary>
    /// <remarks>
    /// The router keeps the list as a set and prints it in its own order (<c>mcs-3,mcs-0,mcs-1</c> reads back <c>mcs-0,mcs-1,mcs-3</c>), so a list merge compares it by members.
    /// MCS 0–7 use one spatial stream, 8–15 two and 16–23 three.
    /// </remarks>
    public enum WirelessHtMcs
    {
        /// <summary>mcs-0.</summary>
        [TikEnum("mcs-0")] Mcs0,
        /// <summary>mcs-1.</summary>
        [TikEnum("mcs-1")] Mcs1,
        /// <summary>mcs-2.</summary>
        [TikEnum("mcs-2")] Mcs2,
        /// <summary>mcs-3.</summary>
        [TikEnum("mcs-3")] Mcs3,
        /// <summary>mcs-4.</summary>
        [TikEnum("mcs-4")] Mcs4,
        /// <summary>mcs-5.</summary>
        [TikEnum("mcs-5")] Mcs5,
        /// <summary>mcs-6.</summary>
        [TikEnum("mcs-6")] Mcs6,
        /// <summary>mcs-7.</summary>
        [TikEnum("mcs-7")] Mcs7,
        /// <summary>mcs-8.</summary>
        [TikEnum("mcs-8")] Mcs8,
        /// <summary>mcs-9.</summary>
        [TikEnum("mcs-9")] Mcs9,
        /// <summary>mcs-10.</summary>
        [TikEnum("mcs-10")] Mcs10,
        /// <summary>mcs-11.</summary>
        [TikEnum("mcs-11")] Mcs11,
        /// <summary>mcs-12.</summary>
        [TikEnum("mcs-12")] Mcs12,
        /// <summary>mcs-13.</summary>
        [TikEnum("mcs-13")] Mcs13,
        /// <summary>mcs-14.</summary>
        [TikEnum("mcs-14")] Mcs14,
        /// <summary>mcs-15.</summary>
        [TikEnum("mcs-15")] Mcs15,
        /// <summary>mcs-16.</summary>
        [TikEnum("mcs-16")] Mcs16,
        /// <summary>mcs-17.</summary>
        [TikEnum("mcs-17")] Mcs17,
        /// <summary>mcs-18.</summary>
        [TikEnum("mcs-18")] Mcs18,
        /// <summary>mcs-19.</summary>
        [TikEnum("mcs-19")] Mcs19,
        /// <summary>mcs-20.</summary>
        [TikEnum("mcs-20")] Mcs20,
        /// <summary>mcs-21.</summary>
        [TikEnum("mcs-21")] Mcs21,
        /// <summary>mcs-22.</summary>
        [TikEnum("mcs-22")] Mcs22,
        /// <summary>mcs-23.</summary>
        [TikEnum("mcs-23")] Mcs23,
    }
}
