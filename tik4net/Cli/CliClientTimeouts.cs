using System;

namespace tik4net.Cli
{
    /// <summary>The timeouts a terminal client works with. Receive is read per use: ReceiveTimeout may change while open.</summary>
    internal readonly struct CliClientTimeouts
    {
        internal CliClientTimeouts(Func<int> receiveMs, int connectMs, int sendMs)
        {
            ReceiveMs = receiveMs;
            ConnectMs = connectMs;
            SendMs = sendMs;
        }

        /// <summary>How long the router may take to answer (ms), read at each use.</summary>
        internal Func<int> ReceiveMs { get; }

        /// <summary>Connect and login (ms); 0 = the client's own fallback.</summary>
        internal int ConnectMs { get; }

        /// <summary>How long a write may block (ms); 0 = the carrier decides.</summary>
        internal int SendMs { get; }

        /// <summary>Fixed values — the unit tests' form.</summary>
        internal static CliClientTimeouts Fixed(int receiveMs, int connectMs = 0, int sendMs = 0)
            => new CliClientTimeouts(() => receiveMs, connectMs, sendMs);
    }
}
