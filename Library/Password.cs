using System;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace InvertedTomato.TikLink {
    public static class Password {
        public static string Hash(string password, string challenge) {
            if (null == password) {
                throw new ArgumentNullException(nameof(password));
            }
            if (null == challenge) {
                throw new ArgumentNullException(nameof(challenge));
            }

            // Decode hex challenge to byte array
            var challengeBytes = new byte[challenge.Length / 2];
            for (var i = 0; i <= challenge.Length - 2; i += 2) {
                challengeBytes[i / 2] = Byte.Parse(challenge.Substring(i, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
            }

            // Concatenate password and challenge
            var payload = new byte[1 + password.Length + challengeBytes.Length];
            payload[0] = 0;
            Encoding.ASCII.GetBytes(password.ToCharArray()).CopyTo(payload, 1);
            challengeBytes.CopyTo(payload, 1 + password.Length);

            // MD5 hash response
            byte[] responseBytes;
            using (var md5 = MD5.Create()) {
                responseBytes = md5.ComputeHash(payload);
            }

            // Encode response in hex
            string result = string.Empty;
            foreach (var h in responseBytes) {
                result += h.ToString("x2", CultureInfo.InvariantCulture);
            }
            return "00" + result;
        }
    }
}
