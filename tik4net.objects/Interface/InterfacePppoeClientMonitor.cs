using System.Diagnostics.CodeAnalysis;

namespace tik4net.Objects.Interface
{
    /// <summary>
    /// /interface/pppoe-client/monitor
    /// Read-only snapshot of the runtime state of a PPPoE client interface, as reported by the "monitor" command.
    /// </summary>
    [TikEntity("/interface/pppoe-client/monitor", LoadCommand ="", LoadDefaultParameterFormat = TikCommandParameterFormat.NameValue, IncludeDetails = false, IsReadOnly = true)]
    public class InterfacePppoeClientMonitor
    {
        /// <summary>status — Current connection status of the PPPoE client (read-only).</summary>
        [TikProperty("status", IsMandatory = true, IsReadOnly = true)]
        public string? Status { get; private set; }

        /// <summary>uptime — How long the current connection has been up (read-only).</summary>
        [TikProperty("uptime", IsMandatory = false, IsReadOnly = true)]
        public TikDuration? Uptime { get; private set; }

        /// <summary>active-links — Number of active PPPoE links (read-only).</summary>
        [TikProperty("active-links", IsMandatory = false, IsReadOnly = true)]
        public string? ActiveLinks { get; private set; }

        /// <summary>encoding — Encoding negotiated with the access concentrator (read-only).</summary>
        [TikProperty("encoding", IsMandatory = false, IsReadOnly = true)]
        public string? Encoding { get; private set; }

        /// <summary>service-name — Service name of the connected access concentrator (read-only).</summary>
        [TikProperty("service-name", IsMandatory = false, IsReadOnly = true)]
        public string? ServiceName { get; private set; }

        /// <summary>ac-name — Name of the connected access concentrator (read-only).</summary>
        [TikProperty("ac-name", IsMandatory = false, IsReadOnly = true)]
        public string? AcName { get; private set; }

        /// <summary>ac-mac — MAC address of the connected access concentrator (read-only).</summary>
        [TikProperty("ac-mac", IsMandatory = false, IsReadOnly = true)]
        public string? AcMac { get; private set; }

        /// <summary>mtu — Maximum Transmit Unit negotiated for the current connection (read-only).</summary>
        [TikProperty("mtu", IsMandatory = false, IsReadOnly = true)]
        public string? Mtu { get; private set; }

        /// <summary>mru — Maximum Receive Unit negotiated for the current connection (read-only).</summary>
        [TikProperty("mru", IsMandatory = false, IsReadOnly = true)]
        public string? Mru { get; private set; }

        /// <summary>local-address — Local IP address assigned for the current connection (read-only).</summary>
        [TikProperty("local-address", IsMandatory = false, IsReadOnly = true)]
        public string? LocalAddress { get; private set; }

        /// <summary>remote-address — Remote (server) IP address for the current connection (read-only).</summary>
        [TikProperty("remote-address", IsMandatory = false, IsReadOnly = true)]
        public string? RemoteAddress { get; private set; }

        /// <summary>
        /// Gets snapshot of actual values for given <paramref name="interfaceName"/>.
        /// </summary>
        [RequiresUnreferencedCode(TikTrimming.MapperMessage)]
        [RequiresDynamicCode(TikTrimming.DynamicCodeMessage)]
        public static InterfacePppoeClientMonitor GetSnapshot(ITikConnection connection, string interfaceName)
        {
            return connection.GetInterfacePppoeClientMonitorSnapshot(interfaceName);
        }
    }

    /// <summary>
    /// Connection extension class for <see cref="InterfacePppoeClientMonitor"/>
    /// </summary>
    [RequiresUnreferencedCode(TikTrimming.MapperMessage)]
    [RequiresDynamicCode(TikTrimming.DynamicCodeMessage)]
    public static class InterfacePppoeClientMonitorConnectionExtensions
    {
        /// <summary>
        /// Gets snapshot of actual values for given <paramref name="interfaceName"/>.
        /// </summary>
        public static InterfacePppoeClientMonitor GetInterfacePppoeClientMonitorSnapshot(this ITikConnection connection, string interfaceName)
        {
            var result = connection.LoadSingle<InterfacePppoeClientMonitor>(
                connection.CreateParameter("numbers", interfaceName, TikCommandParameterFormat.NameValue),
                connection.CreateParameter("once", "", TikCommandParameterFormat.NameValue));

            return result;
        }
    }
}
