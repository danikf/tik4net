namespace tik4net
{
    /// <summary>
    /// Implemented by the SSH transport: the options of an SSH session — logging in with a private key, and pinning the
    /// router's host key.
    /// </summary>
    /// <remarks>
    /// <see cref="TikConnectionSetup"/> applies <see cref="TikConnectionSetup.SshPrivateKey"/> through this interface, and
    /// refuses a key for a connection that does not implement it: a key is a credential, and a transport that dropped it
    /// would log in with the password instead, or fail with a password error for a user that has none.
    /// <see cref="TikConnectionSetup.SshHostKeyFingerprint"/> goes through it too, and is a setting rather than a credential:
    /// the transports that do not implement this interface have no SSH host key to check, and do not receive it.
    /// </remarks>
    public interface ITikSshKeyConnection
    {
        /// <summary>
        /// The private key to log in with, as the text of the key file (OpenSSH or PEM, e.g. <c>-----BEGIN OPENSSH
        /// PRIVATE KEY-----</c>…), or <c>null</c> to log in with the password only. Must be set before the connection is
        /// opened.
        /// </summary>
        /// <remarks>
        /// With a key, the key is tried first, and the password second when one is given. RouterOS keeps a user's public
        /// keys under <c>/user/ssh-keys</c>; a user that has one cannot log in with a password unless
        /// <c>/ip/ssh</c> <c>always-allow-password-login</c> is on.
        /// </remarks>
        string? SshPrivateKey { get; set; }

        /// <summary>The passphrase of an encrypted <see cref="SshPrivateKey"/>, or <c>null</c> when it has none.</summary>
        string? SshPrivateKeyPassphrase { get; set; }

        /// <summary>
        /// The router's SSH host key pinned by its SHA-256 fingerprint, or <c>null</c> (the default) to accept any host
        /// key. Must be set before the connection is opened.
        /// </summary>
        /// <remarks>
        /// <para>The form <c>ssh-keygen -lf</c> prints and RouterOS 7 shows as <c>host-key-fingerprint</c> in
        /// <c>/ip ssh print</c>: <c>SHA256:</c> and the unpadded base64 of the digest
        /// (<c>SHA256:47DEQpj8HBSa+/TImW+5JCeuQeRkm5NMpJWZG3hSuFU</c>). The prefix may be left out.</para>
        /// <para>When set, a host key with another fingerprint fails <c>Open</c> with
        /// <see cref="TikConnectionHostKeyMismatchException"/> during the key exchange, before the password or the key
        /// is sent; a value not in this form fails it with <see cref="System.ArgumentException"/>. When <c>null</c>, SSH
        /// encrypts the session but does not authenticate the router: a man in the middle receives the password.
        /// Through a RoMON agent the SSH session is the agent's, so this pins the agent's host key.
        /// <c>/ip ssh regenerate-host-key</c> changes it.</para>
        /// </remarks>
        string? SshHostKeyFingerprint { get; set; }
    }
}
