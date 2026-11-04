namespace tik4net.Objects.Ip.Ipsec
{
    /// <summary>A Diffie-Hellman group, a member of <see cref="IpsecProfile.DhGroup"/>.</summary>
    /// <remarks>
    /// The router prints the list in its own order (<c>ecp256,modp1024,modp2048</c> reads back
    /// <c>ecp256,modp2048,modp1024</c>). <see cref="Ec2n155"/> and <see cref="Ec2n185"/> are refused by RouterOS 7.
    /// </remarks>
    public enum IpsecDhGroup
    {
        /// <summary>modp768 — 768-bit MODP group (group 1).</summary>
        [TikEnum("modp768")] Modp768,
        /// <summary>modp1024 — 1024-bit MODP group (group 2).</summary>
        [TikEnum("modp1024")] Modp1024,
        /// <summary>modp1536 — 1536-bit MODP group (group 5).</summary>
        [TikEnum("modp1536")] Modp1536,
        /// <summary>modp2048 — 2048-bit MODP group (group 14).</summary>
        [TikEnum("modp2048")] Modp2048,
        /// <summary>modp3072 — 3072-bit MODP group (group 15).</summary>
        [TikEnum("modp3072")] Modp3072,
        /// <summary>modp4096 — 4096-bit MODP group (group 16).</summary>
        [TikEnum("modp4096")] Modp4096,
        /// <summary>modp6144 — 6144-bit MODP group (group 17).</summary>
        [TikEnum("modp6144")] Modp6144,
        /// <summary>modp8192 — 8192-bit MODP group (group 18).</summary>
        [TikEnum("modp8192")] Modp8192,
        /// <summary>ecp256 — 256-bit elliptic curve group (group 19).</summary>
        [TikEnum("ecp256")] Ecp256,
        /// <summary>ecp384 — 384-bit elliptic curve group (group 20).</summary>
        [TikEnum("ecp384")] Ecp384,
        /// <summary>ecp521 — 521-bit elliptic curve group (group 21).</summary>
        [TikEnum("ecp521")] Ecp521,
        /// <summary>ec2n155 — 155-bit EC2N group (group 3). RouterOS 6 only.</summary>
        [TikEnum("ec2n155")] Ec2n155,
        /// <summary>ec2n185 — 185-bit EC2N group (group 4). RouterOS 6 only.</summary>
        [TikEnum("ec2n185")] Ec2n185,
    }
}
