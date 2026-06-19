namespace tik4net.Objects.Ip.Ipsec
{
    /// <summary>
    /// /ip/ipsec/key/rsa
    ///
    /// RSA key store for IPsec peer authentication. Keys are created via the
    /// <c>generate-key</c> action (name + key-size), imported from a file via <c>import</c>,
    /// or exported as a public-key file via <c>export-pub-key</c>. The table is read/write
    /// (rows can be removed and renamed) but there is no plain <c>add</c> command — all new
    /// keys must go through <c>generate-key</c> or <c>import</c>.
    ///
    /// Supports the <c>rsa-key</c> and <c>rsa-signature-hybrid</c> authentication methods
    /// in <c>/ip/ipsec/identity</c>.
    /// </summary>
    [TikEntity("/ip/ipsec/key/rsa", IncludeDetails = true)]
    public class IpsecKey
    {
        /// <summary>.id — primary key of row</summary>
        [TikProperty(".id", IsReadOnly = true, IsMandatory = true)]
        public string Id { get; private set; }

        /// <summary>
        /// name — identifier for the key; referenced from <c>/ip/ipsec/identity</c> when
        /// using RSA-based authentication methods.
        /// </summary>
        [TikProperty("name", IsMandatory = true)]
        public string Name { get; set; }

        // --- Read-only status properties ---

        /// <summary>
        /// key-size — size of the RSA key in bits (2048, 4096, or 8192).
        /// Set at generation time via <c>generate-key</c> and cannot be changed afterwards.
        /// </summary>
        [TikProperty("key-size", IsReadOnly = true)]
        public string/*bits: 2048|4096|8192*/ KeySize { get; private set; }

        /// <summary>
        /// private-key — true when this entry holds the private key material (i.e. it was
        /// generated locally or imported with a private-key file). false when only the public
        /// key is available (e.g. a peer's imported public key).
        /// </summary>
        [TikProperty("private-key", IsReadOnly = true)]
        public bool PrivateKey { get; private set; }

        /// <summary>
        /// rsa — true when the key is in RSA format (always true for entries in this table).
        /// </summary>
        [TikProperty("rsa", IsReadOnly = true)]
        public bool Rsa { get; private set; }

        /// <summary>Human-readable identity.</summary>
        public override string ToString() => Name;
    }

    /// <summary>Connection extension methods for <see cref="IpsecKey"/>.</summary>
    public static class IpsecKeyConnectionExtensions
    {
        /// <summary>
        /// Generates a new RSA key pair and stores it in <c>/ip/ipsec/key/rsa</c>.
        /// The key is available in the table immediately after this call returns.
        /// </summary>
        /// <param name="connection">Open connection to the router.</param>
        /// <param name="name">Name for the new key (must be unique in the table).</param>
        /// <param name="keySizeBits">RSA key size in bits: "2048", "4096", or "8192". Default: "2048".</param>
        public static void GenerateIpsecKey(
            this ITikConnection connection,
            string name,
            string keySizeBits = "2048")
        {
            var cmd = connection.CreateCommand("/ip/ipsec/key/rsa/generate-key");
            cmd.AddParameter("name", name, TikCommandParameterFormat.NameValue);
            cmd.AddParameter("key-size", keySizeBits, TikCommandParameterFormat.NameValue);
            cmd.ExecuteNonQuery();
        }

        /// <summary>
        /// Exports the public component of an RSA key to a file on the router.
        /// The file can then be downloaded and distributed to remote peers.
        /// </summary>
        /// <param name="connection">Open connection to the router.</param>
        /// <param name="keyName">Name of the key to export (matches <see cref="IpsecKey.Name"/>).</param>
        /// <param name="fileName">Destination file name on the router's file system (without extension).</param>
        public static void ExportIpsecPublicKey(
            this ITikConnection connection,
            string keyName,
            string fileName)
        {
            var cmd = connection.CreateCommand("/ip/ipsec/key/rsa/export-pub-key");
            cmd.AddParameter("key", keyName, TikCommandParameterFormat.NameValue);
            cmd.AddParameter("file-name", fileName, TikCommandParameterFormat.NameValue);
            cmd.ExecuteNonQuery();
        }

        /// <summary>
        /// Imports an RSA key (private or public) from a file on the router's file system.
        /// </summary>
        /// <param name="connection">Open connection to the router.</param>
        /// <param name="name">Name to assign to the imported key.</param>
        /// <param name="fileName">Source file name on the router's file system.</param>
        /// <param name="passphrase">Optional passphrase if the key file is encrypted.</param>
        public static void ImportIpsecKey(
            this ITikConnection connection,
            string name,
            string fileName,
            string passphrase = null)
        {
            var cmd = connection.CreateCommand("/ip/ipsec/key/rsa/import");
            cmd.AddParameter("name", name, TikCommandParameterFormat.NameValue);
            cmd.AddParameter("file-name", fileName, TikCommandParameterFormat.NameValue);
            if (!string.IsNullOrEmpty(passphrase))
                cmd.AddParameter("passphrase", passphrase, TikCommandParameterFormat.NameValue);
            cmd.ExecuteNonQuery();
        }
    }
}
