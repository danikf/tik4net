namespace tik4net.Crypto
{
    /// <summary>
    /// The four stream keys an EC-SRP5 login derives (<see cref="WinboxStreamCrypto.DeriveStreamKeys"/>), as one
    /// value, with the encrypt and decrypt that use them.
    /// </summary>
    internal sealed class WinboxStreamKeys
    {
        private WinboxStreamKeys(byte[] sendAes, byte[] sendHmac, byte[] receiveAes, byte[] receiveHmac)
        {
            SendAes = sendAes;
            SendHmac = sendHmac;
            ReceiveAes = receiveAes;
            ReceiveHmac = receiveHmac;
        }

        internal byte[] SendAes { get; }
        internal byte[] SendHmac { get; }
        internal byte[] ReceiveAes { get; }

        /// <summary>Checks the HMAC of a received frame (<see cref="Decrypt"/>).</summary>
        internal byte[] ReceiveHmac { get; }

        /// <summary>The keys for one side of the session from the EC-SRP5 shared secret.</summary>
        /// <param name="secret">The secret (<see cref="EcSrp5Proof.Secret"/>).</param>
        /// <param name="server">True for the router's side — the send and receive halves swapped.</param>
        internal static WinboxStreamKeys Derive(byte[] secret, bool server = false)
        {
            WinboxStreamCrypto.DeriveStreamKeys(server, secret,
                out byte[] sendAes, out byte[] receiveAes, out byte[] sendHmac, out byte[] receiveHmac);
            return new WinboxStreamKeys(sendAes, sendHmac, receiveAes, receiveHmac);
        }

        /// <summary>Encrypts one outbound message (<see cref="WinboxStreamCrypto.Encrypt(byte[], byte[], byte[])"/>).</summary>
        internal byte[] Encrypt(byte[] message) => WinboxStreamCrypto.Encrypt(message, SendAes, SendHmac);

        /// <summary>
        /// Decrypts one inbound frame and checks its HMAC, or <c>null</c> when it does not decrypt or the HMAC does not
        /// match (<see cref="WinboxStreamCrypto.Decrypt"/>).
        /// </summary>
        internal byte[]? Decrypt(byte[]? frame) => WinboxStreamCrypto.Decrypt(frame, ReceiveAes, ReceiveHmac);
    }
}
