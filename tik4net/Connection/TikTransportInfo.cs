using System;
using System.Collections.Generic;
using tik4net.MacTelnet;
using tik4net.Telnet;
using tik4net.WinboxCli;
using tik4net.WinboxCliMac;
using tik4net.WinboxNative;

namespace tik4net.Connection
{
    /// <summary>
    /// The facts about a built-in transport that several places need: its name in messages and debug output, the port
    /// its parameterless <c>Open</c> dials, whether it runs on the MAC layer, and whether it relays to a router behind a
    /// RoMON agent. One row per <see cref="TikConnectionType"/>, SSH included: the row needs no <c>tik4net.ssh</c>, only
    /// creating the connection does (<see cref="TikConnectionRegistry"/>, which is the factory; this is the facts).
    /// </summary>
    internal sealed class TikTransportInfo
    {
        private TikTransportInfo(TikConnectionType type, string displayName, int defaultPort, bool macLayer, bool relaysRomon)
        {
            Type = type;
            DisplayName = displayName;
            DefaultPort = defaultPort;
            MacLayer = macLayer;
            RelaysRomon = relaysRomon;
        }

        internal TikConnectionType Type { get; }

        /// <summary>The transport's name in messages ("MAC-Telnet connection is not open.") and in debug lines.</summary>
        internal string DisplayName { get; }

        /// <summary>The port the parameterless <c>Open</c> dials.</summary>
        internal int DefaultPort { get; }

        /// <summary>Reaches the router by its MAC address, on the MAC layer.</summary>
        internal bool MacLayer { get; }

        /// <summary>Can reach a router through a RoMON agent (<see cref="TikConnectionSetup.RomonAgentSetup"/>).</summary>
        internal bool RelaysRomon { get; }

        /// <summary>The row of <paramref name="type"/>.</summary>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="type"/> is not a declared value.</exception>
        internal static TikTransportInfo Of(TikConnectionType type)
            => Table.TryGetValue(type, out var info)
                ? info
                : throw new ArgumentOutOfRangeException(nameof(type), type, "Not a declared TikConnectionType value.");

        private static readonly Dictionary<TikConnectionType, TikTransportInfo> Table = Rows(
            new TikTransportInfo(TikConnectionType.Api, "API", 8728, macLayer: false, relaysRomon: false),
            new TikTransportInfo(TikConnectionType.ApiSsl, "API", 8729, macLayer: false, relaysRomon: false),
            new TikTransportInfo(TikConnectionType.Rest, "REST", 80, macLayer: false, relaysRomon: false),
            new TikTransportInfo(TikConnectionType.RestSsl, "REST", 443, macLayer: false, relaysRomon: false),
            new TikTransportInfo(TikConnectionType.Ssh, "SSH", 22, macLayer: false, relaysRomon: true),
            new TikTransportInfo(TikConnectionType.Telnet, "Telnet", TelnetConnection.DefaultPort, macLayer: false, relaysRomon: true),
            new TikTransportInfo(TikConnectionType.MacTelnet, "MAC-Telnet", MacTelnetConnection.DefaultPort, macLayer: true, relaysRomon: true),
            new TikTransportInfo(TikConnectionType.WinboxCli, "WinBox CLI", WinboxCliConnection.DefaultPort, macLayer: false, relaysRomon: true),
            new TikTransportInfo(TikConnectionType.WinboxCliMac, "WinBox CLI MAC", WinboxCliMacConnection.DefaultPort, macLayer: true, relaysRomon: true),
            new TikTransportInfo(TikConnectionType.WinboxNative, "WinBox native", WinboxNativeConnection.DefaultPort, macLayer: false, relaysRomon: true),
            new TikTransportInfo(TikConnectionType.WinboxNativeMac, "WinBox native MAC", 20561, macLayer: true, relaysRomon: true));

        private static Dictionary<TikConnectionType, TikTransportInfo> Rows(params TikTransportInfo[] rows)
        {
            var table = new Dictionary<TikConnectionType, TikTransportInfo>();
            foreach (var row in rows)
                table.Add(row.Type, row);
            return table;
        }
    }
}
