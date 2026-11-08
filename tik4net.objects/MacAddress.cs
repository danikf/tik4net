using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace tik4net.Objects
{
    /// <summary>
    /// MAC address in format 00:00:00:00:00:00
    /// </summary>
    public class MacAddress
    {
        private const string DEFAULT_MAC = "00:00:00:00:00:00";
        private string _address;

        /// <summary>
        /// MAC as string in format 00:00:00:00:00:00; <c>null</c> for a <c>null</c> instance.
        /// </summary>
        [return: global::System.Diagnostics.CodeAnalysis.NotNullIfNotNull(nameof(mac))]
        public static implicit operator string?(MacAddress? mac) { return mac?.Address; }

        /// <summary>
        /// MAC from a string in format 00:00:00:00:00:00, as <see cref="MacAddress(string)"/>.
        /// </summary>
        /// <exception cref="ArgumentException"><paramref name="mac"/> is null, empty, or not a MAC address.</exception>
        public static implicit operator MacAddress(string mac)
        {
            return new MacAddress(mac);
        }

        /// <summary>
        /// MAC address in format 00:00:00:00:00:00
        /// </summary>
        public string Address
        {
            get { return _address; }
            set
            {
                EnsureValidMac(value);
                _address = value;
            }
        }

        /// <summary>
        /// The zero MAC, 00:00:00:00:00:00, spelled explicitly.
        /// </summary>
        public MacAddress()
        {
            _address = DEFAULT_MAC;
        }

        /// <summary>
        /// A MAC address from its text.
        /// </summary>
        /// <param name="mac">MAC address in format 00:00:00:00:00:00</param>
        /// <exception cref="ArgumentException">
        /// <paramref name="mac"/> is null, empty, or not a MAC address. Use <see cref="MacAddress()"/> for the zero MAC.
        /// </exception>
        public MacAddress(string mac)
        {
            EnsureValidMac(mac);
            _address = mac;
        }

        /// <summary>Reads <c>AA:BB:CC:DD:EE:FF</c>.</summary>
        /// <exception cref="FormatException"><paramref name="mac"/> is not a MAC address.</exception>
        public static MacAddress Parse(string mac)
        {
            if (mac != null && MacRegex.IsMatch(mac))
                return new MacAddress(mac);
            throw new FormatException(string.Format(CultureInfo.InvariantCulture, "Invalid MAC address '{0}'.", mac));
        }

        /// <summary><see cref="Parse"/> without the exception.</summary>
        public static bool TryParse(string? mac, out MacAddress? result)
        {
            result = mac != null && MacRegex.IsMatch(mac) ? new MacAddress(mac) : null;
            return result != null;
        }

        /// <summary>The address in format 00:00:00:00:00:00, as the router spells it.</summary>
        public override string ToString() => _address;

        private static Regex MacRegex = new Regex(@"^[\da-f]{2}:[\da-f]{2}:[\da-f]{2}:[\da-f]{2}:[\da-f]{2}:[\da-f]{2}$", RegexOptions.IgnoreCase);
        private static void EnsureValidMac(string? mac)
        {
            if (mac == null || !MacRegex.IsMatch(mac))
                throw new ArgumentException(string.Format(CultureInfo.InvariantCulture, "Invalid MAC address '{0}'.", mac));
        }
    }
}
