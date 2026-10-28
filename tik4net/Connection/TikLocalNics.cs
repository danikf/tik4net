using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace tik4net.Connection
{
    /// <summary>
    /// The local network interfaces as MNDP discovery and the MAC-layer transports see them: which ones are worth
    /// sending from, their MAC, their IPv4 addresses, and the subnet arithmetic over them.
    /// </summary>
    /// <remarks>
    /// Real adapters report what a tidy model would not: an IPv4 address with no mask, or an all-zero one (some VPN
    /// and virtual adapters), and a MAC of all zeroes. Every method here takes that as a value to step over, never as
    /// a reason to fail.
    /// </remarks>
    internal static class TikLocalNics
    {
        /// <summary>Up, not loopback, not a tunnel — the interfaces a broadcast is worth sending from.</summary>
        internal static IEnumerable<NetworkInterface> Eligible()
        {
            NetworkInterface[] all;
            try { all = NetworkInterface.GetAllNetworkInterfaces(); }
            catch (NetworkInformationException) { yield break; }

            foreach (var ni in all)
            {
                if (ni.OperationalStatus != OperationalStatus.Up) continue;
                if (ni.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;
                if (ni.NetworkInterfaceType == NetworkInterfaceType.Tunnel) continue;

                yield return ni;
            }
        }

        /// <summary>The interface's 6-byte MAC, or <c>null</c> when it has none (wrong length, or all zeroes).</summary>
        internal static byte[]? MacOf(NetworkInterface ni)
        {
            byte[] mac = ni.GetPhysicalAddress().GetAddressBytes();
            return mac.Length == 6 && mac.Any(b => b != 0) ? mac : null;
        }

        /// <summary>The interface's IPv4 addresses, in the order it reports them.</summary>
        internal static IEnumerable<UnicastIPAddressInformation> IPv4Addresses(NetworkInterface ni)
            => ni.GetIPProperties().UnicastAddresses.Where(ua => ua.Address.AddressFamily == AddressFamily.InterNetwork);

        /// <summary>
        /// Whether <paramref name="target"/> lies in the subnet of <paramref name="address"/>; <c>false</c> when the
        /// address has no mask to tell.
        /// </summary>
        internal static bool InSubnet(UnicastIPAddressInformation address, IPAddress target)
        {
            IPAddress? mask = address.IPv4Mask;
            if (mask == null) return false;
            byte[] lb = address.Address.GetAddressBytes(), mb = mask.GetAddressBytes(), tb = target.GetAddressBytes();
            if (lb.Length != 4 || mb.Length != 4 || tb.Length != 4) return false;
            for (int i = 0; i < 4; i++)
                if ((lb[i] & mb[i]) != (tb[i] & mb[i])) return false;
            return true;
        }

        /// <summary>
        /// The directed (subnet) broadcast address for an IPv4 address and its mask — the host bits set —
        /// or <c>null</c> when the pair does not describe a subnet a broadcast could be addressed to.
        /// </summary>
        /// <remarks>
        /// A mask of all zeroes is rejected rather than turned into 255.255.255.255: that is the limited
        /// broadcast, which is exactly the address whose routing a directed broadcast exists to avoid depending on.
        /// </remarks>
        /// <param name="address">An IPv4 address in the subnet (the interface's own, or a host in it).</param>
        /// <param name="mask">The subnet mask.</param>
        internal static IPAddress? SubnetBroadcast(IPAddress? address, IPAddress? mask)
        {
            if (address == null || mask == null) return null;
            if (address.AddressFamily != AddressFamily.InterNetwork) return null;

            byte[] a = address.GetAddressBytes();
            byte[] m = mask.GetAddressBytes();
            if (a.Length != 4 || m.Length != 4) return null;
            if (m.All(b => b == 0)) return null;   // no usable subnet

            var broadcast = new byte[4];
            for (int i = 0; i < 4; i++)
                broadcast[i] = (byte)(a[i] | ~m[i]);

            return new IPAddress(broadcast);
        }
    }
}
