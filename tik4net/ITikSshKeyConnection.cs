namespace tik4net
{
    /// <summary>
    /// Implemented by connections that can log in with an SSH private key — the SSH transport.
    /// </summary>
    /// <remarks>
    /// <see cref="TikConnectionSetup"/> applies <see cref="TikConnectionSetup.SshPrivateKey"/> through this interface, and
    /// refuses a key for a connection that does not implement it: a key is a credential, and a transport that dropped it
    /// would log in with the password instead, or fail with a password error for a user that has none.
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
    }
}
