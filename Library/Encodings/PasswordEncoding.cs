using System;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace InvertedTomato.TikLink.Encodings {
    public static class PasswordEncoding {
        public static string Hash(string password, string challenge) { // TODO: Optimise
            var hash_byte = new byte[challenge.Length / 2];
            for (int i = 0; i <= challenge.Length - 2; i += 2) {
                hash_byte[i / 2] = Byte.Parse(challenge.Substring(i, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
            }

            var heslo = new byte[1 + password.Length + hash_byte.Length];
            heslo[0] = 0;
            Encoding.ASCII.GetBytes(password.ToCharArray()).CopyTo(heslo, 1);
            hash_byte.CopyTo(heslo, 1 + password.Length);

            byte[] hotovo;
            using (var md5 = MD5.Create()) {
                hotovo = md5.ComputeHash(heslo);
            }

            // Convert encoded bytes back to a 'readable' string
            string result = "";
            foreach (var h in hotovo) {
                result += h.ToString("x2", CultureInfo.InvariantCulture);
            }
            return "00" + result;
        }
    }
}
