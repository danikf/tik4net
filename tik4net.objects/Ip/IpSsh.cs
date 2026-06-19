using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace tik4net.Objects.Ip
{
    /// <summary>
    /// /ip/ssh: SSH server configuration (singleton). Controls which ciphers, key types, and
    /// authentication methods the RouterOS SSH server accepts.
    /// Use <see cref="TikConnectionExtensions.LoadSingle{T}"/> to load.
    /// </summary>
    [TikEntity("/ip/ssh", IsSingleton = true)]
    public class IpSsh
    {
        /// <summary>strong-crypto — enables stronger encryption algorithms and larger DH prime groups.</summary>
        [TikProperty("strong-crypto", DefaultValue = "no")]
        public bool StrongCrypto { get; set; }

        /// <summary>ciphers — SSH cipher suite selection. Default: auto (lets RouterOS pick the best available).
        /// <seealso cref="SshCiphers"/></summary>
        [TikProperty("ciphers", DefaultValue = "auto")]
        public SshCiphers Ciphers { get; set; }

        /// <summary>forwarding-enabled — controls which SSH port-forwarding modes are permitted.
        /// <seealso cref="SshForwardingMode"/></summary>
        [TikProperty("forwarding-enabled", DefaultValue = "no")]
        public SshForwardingMode ForwardingEnabled { get; set; }

        /// <summary>host-key-size — RSA host key size in bits, applied at next key regeneration. Default: 2048.</summary>
        [TikProperty("host-key-size", DefaultValue = "2048")]
        public int HostKeySize { get; set; }

        /// <summary>host-key-type — host key algorithm type.
        /// <seealso cref="SshHostKeyType"/></summary>
        [TikProperty("host-key-type", DefaultValue = "rsa")]
        public SshHostKeyType HostKeyType { get; set; }

        /// <summary>password-authentication — controls whether password login is allowed alongside public-key auth.
        /// <seealso cref="SshPasswordAuth"/></summary>
        [TikProperty("password-authentication", DefaultValue = "yes-if-no-key")]
        public SshPasswordAuth PasswordAuthentication { get; set; }

        /// <summary>publickey-authentication-options — additional requirements for public-key authentication.
        /// <seealso cref="SshPubkeyOptions"/></summary>
        [TikProperty("publickey-authentication-options", DefaultValue = "none")]
        public SshPubkeyOptions PublickeyAuthenticationOptions { get; set; }

        /// <summary>Human-readable SSH settings summary.</summary>
        public override string ToString() => string.Format("ciphers={0} forwarding={1} strong-crypto={2}", Ciphers, ForwardingEnabled, StrongCrypto);
    }

    /// <summary>SSH cipher selection for <see cref="IpSsh.Ciphers"/>.</summary>
    public enum SshCiphers
    {
        /// <summary>auto — RouterOS picks the best available cipher.</summary>
        [TikEnum("auto")] Auto,
        /// <summary>aes-gcm — AES-GCM authenticated encryption.</summary>
        [TikEnum("aes-gcm")] AesGcm,
        /// <summary>aes-ctr — AES counter mode.</summary>
        [TikEnum("aes-ctr")] AesCtr,
        /// <summary>aes-cbc — AES cipher block chaining.</summary>
        [TikEnum("aes-cbc")] AesCbc,
        /// <summary>3des-cbc — Triple DES cipher block chaining (legacy).</summary>
        [TikEnum("3des-cbc")] TripleDesCbc,
        /// <summary>null — no encryption (testing only).</summary>
        [TikEnum("null")] Null,
    }

    /// <summary>SSH forwarding mode for <see cref="IpSsh.ForwardingEnabled"/>.</summary>
    public enum SshForwardingMode
    {
        /// <summary>no — port forwarding disabled.</summary>
        [TikEnum("no")] No,
        /// <summary>local — local (outbound) port forwarding only.</summary>
        [TikEnum("local")] Local,
        /// <summary>remote — remote (inbound) port forwarding only.</summary>
        [TikEnum("remote")] Remote,
        /// <summary>both — local and remote forwarding permitted.</summary>
        [TikEnum("both")] Both,
    }

    /// <summary>SSH host key algorithm for <see cref="IpSsh.HostKeyType"/>.</summary>
    public enum SshHostKeyType
    {
        /// <summary>rsa — RSA host key.</summary>
        [TikEnum("rsa")] Rsa,
        /// <summary>ed25519 — Ed25519 host key (smaller, faster).</summary>
        [TikEnum("ed25519")] Ed25519,
    }

    /// <summary>Password authentication mode for <see cref="IpSsh.PasswordAuthentication"/>.</summary>
    public enum SshPasswordAuth
    {
        /// <summary>yes-if-no-key — allow password login only when no public key is configured.</summary>
        [TikEnum("yes-if-no-key")] YesIfNoKey,
        /// <summary>yes — always allow password login.</summary>
        [TikEnum("yes")] Yes,
        /// <summary>no — password login disabled; public-key only.</summary>
        [TikEnum("no")] No,
    }

    /// <summary>Public-key authentication options for <see cref="IpSsh.PublickeyAuthenticationOptions"/>.</summary>
    public enum SshPubkeyOptions
    {
        /// <summary>none — no additional requirements beyond key verification.</summary>
        [TikEnum("none")] None,
        /// <summary>touch-required — hardware security key must be physically touched.</summary>
        [TikEnum("touch-required")] TouchRequired,
        /// <summary>verify-required — hardware security key must verify user presence/PIN.</summary>
        [TikEnum("verify-required")] VerifyRequired,
    }
}
