using System;
using System.Text;

namespace tik4net.Winbox
{
    /// <summary>
    /// Reads the frame the router sends where the EC-SRP5 server confirmation belongs. Shared by the
    /// TCP and MAC-layer handshakes, which carry the identical WinBox exchange over different channels.
    /// </summary>
    internal static class WinboxHandshakeReply
    {
        /// <summary>
        /// Throws if <paramref name="payload"/> is the router talking rather than a confirmation digest:
        /// <see cref="TikConnectionLoginRefusedException"/> when it is readable text,
        /// <see cref="InvalidOperationException"/> when it is neither text nor the right size.
        /// Returns quietly when the payload could be a digest, leaving the comparison to the caller.
        /// </summary>
        /// <remarks>
        /// The size test comes first and the text test second, so a digest is never mistaken for a
        /// message: a 32-byte digest of printable bytes is astronomically unlikely but not impossible,
        /// and misreading one as a refusal would turn a successful login into an error.
        /// </remarks>
        internal static void ThrowIfRouterMessage(byte[] payload, int confirmationLength)
        {
            if (payload != null && payload.Length == confirmationLength) return;

            string text = AsPrintableText(payload);
            if (text != null) throw new TikConnectionLoginRefusedException("WinBox", text);

            throw new InvalidOperationException(
                $"WinBox EC-SRP5 handshake: expected a {confirmationLength} B server confirmation, got " +
                $"{payload?.Length ?? 0} B that is not readable text. The handshake did not complete.");
        }

        /// <summary>The payload as text, or <c>null</c> if any byte is outside printable ASCII.</summary>
        internal static string AsPrintableText(byte[] payload)
        {
            if (payload == null || payload.Length == 0) return null;
            foreach (byte b in payload)
                if (b < 0x20 || b > 0x7E) return null;
            return Encoding.ASCII.GetString(payload);
        }
    }
}
