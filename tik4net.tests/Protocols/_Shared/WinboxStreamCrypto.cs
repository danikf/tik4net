// WinboxStreamCrypto.cs — Key derivation + AES-128-CBC framing helpers
// Shared by WinboxM2Client (TCP) and WinboxMacClient (MAC).
// Extracted from WinboxM2CatalogTest.cs and MacLayerTest.cs.

using System;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace tik4net.tests
{
    /// <summary>
    /// Derives AES-128 + HMAC-SHA1 stream keys from an EC-SRP5 shared secret,
    /// and provides AES-128-CBC MAC-then-Encrypt encrypt/decrypt helpers.
    /// </summary>
    internal static class WinboxStreamCrypto
    {
        // gen_stream_keys from winbox_terminal_client.py
        internal static void DeriveStreamKeys(bool server, byte[] secret,
            out byte[] sendAes, out byte[] recvAes,
            out byte[] sendHmac, out byte[] recvHmac)
        {
            byte[] m2 = Encoding.ASCII.GetBytes(
                "On the client side, this is the send key; on the server side, it is the receive key.");
            byte[] m3 = Encoding.ASCII.GetBytes(
                "On the client side, this is the receive key; on the server side, it is the send key.");
            byte[] f2 = Enumerable.Repeat((byte)0xf2, 40).ToArray();
            byte[] z40 = new byte[40];

            byte[] txRaw = secret.Concat(z40).Concat(server ? m3 : m2).Concat(f2).ToArray();
            byte[] rxRaw = secret.Concat(z40).Concat(server ? m2 : m3).Concat(f2).ToArray();

            byte[] txEnc = EcSrp5.Sha1(txRaw).Take(16).ToArray();
            byte[] rxEnc = EcSrp5.Sha1(rxRaw).Take(16).ToArray();

            byte[] sk = HkdfExpand(txEnc);
            byte[] rk = HkdfExpand(rxEnc);

            sendAes  = sk.Take(16).ToArray();
            sendHmac = sk.Skip(16).Take(20).ToArray();
            recvAes  = rk.Take(16).ToArray();
            recvHmac = rk.Skip(16).Take(20).ToArray();
        }

        // Custom HKDF from winbox_terminal_client.py:
        // h1 = HMAC-SHA1(key=0x00*64, data=msg)
        // round_i = HMAC-SHA1(key=h1, data=prev_round || (i+1))
        internal static byte[] HkdfExpand(byte[] message)
        {
            byte[] h1;
            using (var hmac = new HMACSHA1(new byte[0x40]))
                h1 = hmac.ComputeHash(message);

            byte[] h2 = new byte[0];
            byte[] res = new byte[0];
            for (int i = 0; i < 2; i++)
            {
                byte[] input = h2.Concat(new byte[] { (byte)(i + 1) }).ToArray();
                using (var hmac = new HMACSHA1(h1))
                    h2 = hmac.ComputeHash(input);
                res = res.Concat(h2).ToArray();
            }
            return res.Take(0x24).ToArray();  // 36 bytes
        }

        // MAC-then-Encrypt: append HMAC-SHA1, custom-pad to 16B, AES-128-CBC encrypt.
        // Returns [enc_len 2B BE][IV 16B][ciphertext].
        internal static byte[] Encrypt(byte[] msg, byte[] aesKey, byte[] hmacKey)
        {
            byte[] hmac;
            using (var h = new HMACSHA1(hmacKey))
                hmac = h.ComputeHash(msg);

            byte[] toEnc = msg.Concat(hmac).ToArray();
            int padByte = 0x0F - (toEnc.Length % 0x10);
            toEnc = toEnc.Concat(Enumerable.Repeat((byte)padByte, padByte + 1)).ToArray();

            byte[] iv = new byte[16];
            using (var rng = RandomNumberGenerator.Create()) rng.GetBytes(iv);

            byte[] ciphertext;
            using (var aes = Aes.Create())
            {
                aes.Mode = CipherMode.CBC;
                aes.Padding = PaddingMode.None;
                aes.Key = aesKey;
                aes.IV = iv;
                using (var enc = aes.CreateEncryptor())
                    ciphertext = enc.TransformFinalBlock(toEnc, 0, toEnc.Length);
            }

            // Wire: [enc_len 2B BE][IV 16B][ciphertext]
            return BitConverter.GetBytes((ushort)ciphertext.Length).Reverse().ToArray()
                .Concat(iv).Concat(ciphertext).ToArray();
        }

        // Decrypt [enc_len 2B BE][IV 16B][ciphertext], strip padding + HMAC, return plaintext.
        // Returns null on error (wrong key, padding fault, etc.)
        internal static byte[] Decrypt(byte[] assembled, byte[] aesKey)
        {
            if (assembled == null || assembled.Length < 18) return null;
            byte[] iv         = assembled.Skip(2).Take(16).ToArray();
            byte[] ciphertext = assembled.Skip(18).ToArray();
            if (ciphertext.Length == 0 || ciphertext.Length % 16 != 0) return null;

            byte[] plain;
            using (var aes = Aes.Create())
            {
                aes.Mode = CipherMode.CBC;
                aes.Padding = PaddingMode.None;
                aes.Key = aesKey;
                aes.IV = iv;
                using (var dec = aes.CreateDecryptor())
                    plain = dec.TransformFinalBlock(ciphertext, 0, ciphertext.Length);
            }

            // Strip padding: last byte = padByte, strip padByte+1 bytes total
            if (plain.Length == 0) return null;
            int strip = plain[plain.Length - 1] + 1;
            if (strip > plain.Length) return null;
            plain = plain.Take(plain.Length - strip).ToArray();

            // Strip HMAC-SHA1 (20 bytes)
            if (plain.Length < 20) return null;
            return plain.Take(plain.Length - 20).ToArray();
        }
    }
}
