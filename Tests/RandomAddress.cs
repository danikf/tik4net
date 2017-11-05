using System;
using System.Linq;

namespace Tests {
    public static class RandomAddress {
        public static string GenerateMacAddress() {
            var random = new Random();
            var buffer = new byte[6];
            random.NextBytes(buffer);
            buffer[0] = 0;
            buffer[1] = 0;
            buffer[2] = 0;
            var result = String.Concat(buffer.Select(x => string.Format("{0}:", x.ToString("X2"))).ToArray());
            return result.TrimEnd(':');
        }
        
        public static string GenerateIpAddress() {
            var random = new Random();
            var buffer = new byte[4];
            random.NextBytes(buffer);
            buffer[0] = 0;
            var result = String.Concat(buffer.Select(x => string.Format("{0}.", x.ToString())).ToArray());
            return result.TrimEnd('.');
        }
    }
}
