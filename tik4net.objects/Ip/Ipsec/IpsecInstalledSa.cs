namespace tik4net.Objects.Ip.Ipsec
{
    /// <summary>
    /// /ip/ipsec/installed-sa
    ///
    /// Read-only status table of currently installed IPsec Security Associations (SAs).
    /// Each row represents one SA kernel entry, showing the cryptographic algorithms and
    /// keys negotiated during Phase 2, traffic counters, addressing, lifetime, state,
    /// and whether hardware acceleration (AEAD) is active.
    /// Sensitive fields (auth-key, enc-key) are only visible when <c>show-sensitive</c>
    /// is passed; they are mapped as strings and may be empty in normal print output.
    /// </summary>
    [TikEntity("/ip/ipsec/installed-sa", SupportedOperations = TikEntityOperations.None, IncludeDetails = true)]
    public class IpsecInstalledSa
    {
        /// <summary>.id — primary key of row</summary>
        [TikProperty(".id", IsReadOnly = true, IsMandatory = true)]
        public string? Id { get; private set; }

        /// <summary>
        /// src-address — the source address of this SA.
        /// </summary>
        [TikProperty("src-address", IsReadOnly = true, WinboxLabel = "Src. Address")]
        public TikValue<string?> SrcAddress { get; private set; }

        /// <summary>
        /// dst-address — the destination address of this SA.
        /// </summary>
        [TikProperty("dst-address", IsReadOnly = true, WinboxLabel = "Dst. Address")]
        public TikValue<string?> DstAddress { get; private set; }

        /// <summary>
        /// spi — Security Parameter Index identification tag, uniquely identifies this SA
        /// together with the destination address and protocol.
        /// </summary>
        [TikProperty("spi", IsReadOnly = true, WinboxLabel = "SPI")]
        public TikValue<string?> Spi { get; private set; }

        /// <summary>
        /// state — current state of this SA (e.g. "mature", "dying", "dead").
        /// </summary>
        [TikProperty("state", IsReadOnly = true)]
        public TikValue<string?> State { get; private set; }

        /// <summary>
        /// AH — true when the Authentication Header (AH) protocol is used by this SA.
        /// </summary>
        [TikProperty("AH", IsReadOnly = true)]
        public TikValue<bool?> Ah { get; private set; }

        /// <summary>
        /// ESP — true when the Encapsulating Security Payload (ESP) protocol is used by this SA.
        /// </summary>
        [TikProperty("ESP", IsReadOnly = true)]
        public TikValue<bool?> Esp { get; private set; }

        /// <summary>
        /// auth-algorithm — authentication algorithm negotiated for this SA
        /// (e.g. "md5", "sha1", "sha256", "null").
        /// </summary>
        [TikProperty("auth-algorithm", IsReadOnly = true, WinboxLabel = "Auth. Algorithm")]
        public TikValue<string?> AuthAlgorithm { get; private set; }

        /// <summary>
        /// auth-key — the authentication key in use by this SA (sensitive field).
        /// Only populated when the print is executed with <c>show-sensitive</c>.
        /// </summary>
        [TikProperty("auth-key", IsReadOnly = true, IsSensitive = true, WinboxLabel = "Auth. Key")]
        public TikValue<string?> AuthKey { get; private set; }

        /// <summary>
        /// enc-algorithm — encryption algorithm negotiated for this SA
        /// (e.g. "des", "3des", "aes-cbc", "aes-gcm", "null").
        /// </summary>
        [TikProperty("enc-algorithm", IsReadOnly = true)]
        public TikValue<string?> EncAlgorithm { get; private set; }

        /// <summary>
        /// enc-key — the encryption key in use by this SA (sensitive field).
        /// Only populated when the print is executed with <c>show-sensitive</c>.
        /// </summary>
        [TikProperty("enc-key", IsReadOnly = true, IsSensitive = true)]
        public TikValue<string?> EncKey { get; private set; }

        /// <summary>
        /// enc-key-size — length in bits of the encryption key used by this SA.
        /// </summary>
        [TikProperty("enc-key-size", IsReadOnly = true)]
        public TikValue<int?> EncKeySize { get; private set; }

        /// <summary>
        /// hw-aead — true when this SA is hardware-accelerated (AEAD offload).
        /// </summary>
        [TikProperty("hw-aead", IsReadOnly = true)]
        public TikValue<bool?> HwAead { get; private set; }

        /// <summary>
        /// replay — size of the anti-replay window in bytes for this SA.
        /// </summary>
        [TikProperty("replay", IsReadOnly = true, WinboxLabel = "Replay")]
        public TikValue<int?> Replay { get; private set; }

        /// <summary>
        /// current-bytes — number of bytes processed by this SA since it was installed.
        /// Returned as a 64-bit integer by the router; stored as string to avoid overflow.
        /// </summary>
        [TikProperty("current-bytes", IsReadOnly = true, WinboxLabel = "Current Bytes")]
        public TikValue<string?> CurrentBytes { get; private set; }

        /// <summary>
        /// add-lifetime — configured lifetime of this SA in soft/hard format
        /// (e.g. "1h50m/2h"). The soft threshold triggers rekeying; the hard threshold
        /// causes the SA to be deleted.
        /// </summary>
        [TikProperty("add-lifetime", IsReadOnly = true, WinboxLabel = "Add Lifetime")]
        public TikValue<string?> AddLifetime { get; private set; }

        /// <summary>
        /// addtime — date and time when this SA was installed (e.g. "jan/01/1970 00:00:00").
        /// </summary>
        [TikProperty("addtime", IsReadOnly = true)]
        public TikValue<string?> AddTime { get; private set; }

        /// <summary>
        /// expires-in — remaining time until this SA is rekeyed or expires.
        /// </summary>
        [TikProperty("expires-in", IsReadOnly = true, WinboxLabel = "Expires In")]
        public TikValue<string?> ExpiresIn { get; private set; }

        /// <summary>Human-readable identity.</summary>
        public override string ToString() => string.Format("{0} -> {1} spi={2} ({3})", SrcAddress, DstAddress, Spi, State);
    }
}
