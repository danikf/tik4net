using System;
using System.Collections.Generic;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace InvertedTomato.TikLink.Commands {
    public class Login : Command {
        

        public static bool TryLogin(Link link, string username, string password, out string  message) {
            if (null == link) {
                throw new ArgumentNullException(nameof(link));
            }
            if (null == username) {
                throw new ArgumentNullException(nameof(username));
            }
            if (null == password) {
                throw new ArgumentNullException(nameof(password));
            }

            // Get challenge
            var r1 = link.Call("/login").Wait();
            var challenge = r1.GetDoneAttribute("ret");

            // Compute response
            var hash = EncodePassword(password, challenge);

            // Attempt login
            var r2 = link.Call("/login", new Dictionary<string, string>() { { "name", username }, { "response", hash } }).Wait();
            if (r2.IsError) {
                r2.TryGetTrapAttribute("message", out message);
                return false;
            } else {
                message = null;
                return true;
            }
        }


        private static string EncodePassword(string password, string hash) { // TODO: Optimise
            var hash_byte = new byte[hash.Length / 2];
            for (int i = 0; i <= hash.Length - 2; i += 2) {
                hash_byte[i / 2] = Byte.Parse(hash.Substring(i, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
            }

            var heslo = new byte[1 + password.Length + hash_byte.Length];
            heslo[0] = 0;
            Encoding.ASCII.GetBytes(password.ToCharArray()).CopyTo(heslo, 1);
            hash_byte.CopyTo(heslo, 1 + password.Length);

            byte[] hotovo;
            using (var md5 = MD5.Create()) {
                hotovo = md5.ComputeHash(heslo);
            }

            //Convert encoded bytes back to a 'readable' string
            string result = "";
            foreach (var h in hotovo) {
                result += h.ToString("x2", CultureInfo.InvariantCulture);
            }
            return "00" + result;
        }
    }
}
