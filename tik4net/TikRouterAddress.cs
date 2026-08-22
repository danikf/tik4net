using System;
using System.Globalization;

namespace tik4net
{
    /// <summary>
    /// Where the router is: an IP address / host name, a MAC address, or both. One argument instead of two,
    /// because the two are alternatives rather than a pair — an IP transport needs the host, a MAC-layer
    /// transport needs the MAC, and neither has any use for the other one's coordinate.
    /// </summary>
    /// <remarks>
    /// <para>
    /// It exists because both coordinates are strings, so a constructor overload cannot tell them apart.
    /// The named factories say which one is meant; <see cref="Parse"/> (and the implicit conversion from
    /// <see cref="string"/> that uses it) tells them apart by shape, which is unambiguous: six hex pairs
    /// separated by <c>:</c> or <c>-</c> is a MAC address and cannot be a host name or an IPv6 address,
    /// which needs eight groups or a <c>::</c>.
    /// </para>
    /// <para>
    /// Both together is a legitimate third case, not a contradiction: on a MAC transport the host names the
    /// local network interface to use and the MAC identifies the router, which saves the MNDP discovery
    /// that would otherwise cost up to 5 s per open.
    /// </para>
    /// <example>
    /// <code>
    /// TikRouterAddress.FromHost("192.168.88.1")                              // IP transports, and MAC ones via MNDP
    /// TikRouterAddress.FromMac("AA:BB:CC:DD:EE:FF")                          // MAC transports only — no IP anywhere
    /// TikRouterAddress.FromHostAndMac("192.168.88.1", "AA:BB:CC:DD:EE:FF")   // MAC transports, without the MNDP wait
    /// </code>
    /// </example>
    /// </remarks>
    public readonly struct TikRouterAddress : IEquatable<TikRouterAddress>
    {
        /// <summary>Host name or IP address of the router, or <c>null</c> when the router is addressed by MAC alone.</summary>
        public string? Host { get; }

        /// <summary>
        /// Router MAC address in the normalized <c>"AA:BB:CC:DD:EE:FF"</c> form, or <c>null</c> when the
        /// router is addressed by host alone.
        /// </summary>
        public string? Mac { get; }

        private TikRouterAddress(string? host, string? mac)
        {
            Host = host;
            Mac = mac;
        }

        /// <summary>Whether a host name or IP address is present.</summary>
        public bool HasHost => !string.IsNullOrEmpty(Host);

        /// <summary>Whether a MAC address is present.</summary>
        public bool HasMac => !string.IsNullOrEmpty(Mac);

        /// <summary>Whether neither coordinate is present — the state of a <c>default(TikRouterAddress)</c>.</summary>
        public bool IsEmpty => !HasHost && !HasMac;

        /// <summary>The router at a host name or IP address.</summary>
        /// <param name="host">Host name or IP address.</param>
        public static TikRouterAddress FromHost(string host)
        {
            Guard.ArgumentNotNullOrEmptyString(host, nameof(host));
            return new TikRouterAddress(host, null);
        }

        /// <summary>
        /// The router at a MAC address, with no IP address involved at all — usable only by the MAC-layer
        /// transports (MAC-Telnet, WinBox CLI over MAC, WinBox native over MAC), which is the case they
        /// exist for: a router that has no IP address yet, or none reachable from here.
        /// </summary>
        /// <param name="mac">MAC address as <c>"AA:BB:CC:DD:EE:FF"</c> or <c>"AA-BB-CC-DD-EE-FF"</c>.</param>
        /// <exception cref="ArgumentException"><paramref name="mac"/> is not a MAC address.</exception>
        public static TikRouterAddress FromMac(string mac)
        {
            Guard.ArgumentNotNullOrEmptyString(mac, nameof(mac));
            return new TikRouterAddress(null, NormalizeMacOrThrow(mac, nameof(mac)));
        }

        /// <summary>
        /// The router at both coordinates: on a MAC transport the host selects the local network interface
        /// and the MAC identifies the router, skipping MNDP discovery. On an IP transport only the host is
        /// used.
        /// </summary>
        /// <param name="host">Host name or IP address.</param>
        /// <param name="mac">MAC address as <c>"AA:BB:CC:DD:EE:FF"</c> or <c>"AA-BB-CC-DD-EE-FF"</c>.</param>
        /// <exception cref="ArgumentException"><paramref name="mac"/> is not a MAC address.</exception>
        public static TikRouterAddress FromHostAndMac(string host, string mac)
        {
            Guard.ArgumentNotNullOrEmptyString(host, nameof(host));
            Guard.ArgumentNotNullOrEmptyString(mac, nameof(mac));
            return new TikRouterAddress(host, NormalizeMacOrThrow(mac, nameof(mac)));
        }

        /// <summary>
        /// Reads one string as whichever coordinate it is: a MAC address when it has the shape of one, a
        /// host name or IP address otherwise. For a value that comes from configuration and could be
        /// either; prefer <see cref="FromHost"/> / <see cref="FromMac"/> when the code already knows.
        /// </summary>
        /// <param name="value">Host name, IP address or MAC address.</param>
        public static TikRouterAddress Parse(string value)
        {
            Guard.ArgumentNotNullOrEmptyString(value, nameof(value));
            return TryParseMac(value, out _)
                ? new TikRouterAddress(null, NormalizeMacOrThrow(value, nameof(value)))
                : new TikRouterAddress(value, null);
        }

        /// <summary>
        /// <see cref="Parse"/> without the exception on a null or empty <paramref name="value"/>. There is
        /// no other way for it to fail: anything non-empty that is not a MAC address is taken as a host.
        /// </summary>
        /// <param name="value">Host name, IP address or MAC address.</param>
        /// <param name="address">The parsed address, or <c>default</c>.</param>
        public static bool TryParse(string? value, out TikRouterAddress address)
        {
            if (string.IsNullOrEmpty(value))
            {
                address = default;
                return false;
            }

            address = Parse(value!);
            return true;
        }

        /// <summary>
        /// Reads a bare string as an address through <see cref="Parse"/>, so that
        /// <c>new TikConnectionSetup("192.168.88.1", …)</c> and
        /// <c>new TikConnectionSetup("AA:BB:CC:DD:EE:FF", …)</c> both say what they look like they say.
        /// </summary>
        /// <param name="value">Host name, IP address or MAC address.</param>
        public static implicit operator TikRouterAddress(string value) => Parse(value);

        /// <summary>
        /// Whether <paramref name="value"/> has the shape of a MAC address: six hex pairs separated by
        /// <c>:</c> or <c>-</c>. Deliberately strict — this is what decides how a bare string is read.
        /// </summary>
        /// <param name="value">The string to test.</param>
        /// <param name="mac">The six address bytes, or <c>null</c>.</param>
        internal static bool TryParseMac(string? value, out byte[]? mac)
        {
            mac = null;
            if (value == null || value.Length != 17) return false;

            char sep = value[2];
            if (sep != ':' && sep != '-') return false;

            var bytes = new byte[6];
            for (int i = 0; i < 6; i++)
            {
                int p = i * 3;
                if (i > 0 && value[p - 1] != sep) return false;
                if (!byte.TryParse(value.Substring(p, 2), NumberStyles.HexNumber,
                        CultureInfo.InvariantCulture, out bytes[i]))
                    return false;
            }

            mac = bytes;
            return true;
        }

        private static string NormalizeMacOrThrow(string value, string paramName)
        {
            if (!TryParseMac(value, out byte[]? bytes))
                throw new ArgumentException(
                    "'" + value + "' is not a MAC address. The expected form is \"AA:BB:CC:DD:EE:FF\".", paramName);

            return string.Join(":", Array.ConvertAll(bytes!, b => b.ToString("X2", CultureInfo.InvariantCulture)));
        }

        /// <summary>The coordinates this address carries, for diagnostics and error messages.</summary>
        public override string ToString()
        {
            if (HasHost && HasMac) return Host + " (" + Mac + ")";
            if (HasMac) return Mac!;
            return Host ?? "<empty>";
        }

        /// <inheritdoc/>
        public bool Equals(TikRouterAddress other)
            => string.Equals(Host, other.Host, StringComparison.OrdinalIgnoreCase)
            && string.Equals(Mac, other.Mac, StringComparison.OrdinalIgnoreCase);

        /// <inheritdoc/>
        public override bool Equals(object? obj) => obj is TikRouterAddress other && Equals(other);

        /// <inheritdoc/>
        public override int GetHashCode()
        {
            unchecked
            {
                int h = Host == null ? 0 : StringComparer.OrdinalIgnoreCase.GetHashCode(Host);
                return (h * 397) ^ (Mac == null ? 0 : StringComparer.OrdinalIgnoreCase.GetHashCode(Mac));
            }
        }

        /// <summary>Equality operator — see <see cref="Equals(TikRouterAddress)"/>.</summary>
        public static bool operator ==(TikRouterAddress left, TikRouterAddress right) => left.Equals(right);

        /// <summary>Inequality operator — see <see cref="Equals(TikRouterAddress)"/>.</summary>
        public static bool operator !=(TikRouterAddress left, TikRouterAddress right) => !left.Equals(right);
    }
}
