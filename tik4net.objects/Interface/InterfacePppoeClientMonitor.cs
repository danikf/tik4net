namespace tik4net.Objects.Interface
{
    [TikEntity("/interface/pppoe-client/monitor", LoadCommand ="", LoadDefaultParameneterFormat = TikCommandParameterFormat.NameValue, IncludeDetails = false, IsReadOnly = true)]
    public class InterfacePppoeClientMonitor
    {
        [TikProperty("status", IsMandatory = true, IsReadOnly = true)]
        public string Status { get; private set; }

        [TikProperty("uptime", IsMandatory = false, IsReadOnly = true)]
        public string Uptime { get; private set; }

        [TikProperty("active-links", IsMandatory = false, IsReadOnly = true)]
        public string ActiveLinks { get; private set; }

        [TikProperty("encoding", IsMandatory = false, IsReadOnly = true)]
        public string Encoding { get; private set; }

        [TikProperty("service-name", IsMandatory = false, IsReadOnly = true)]
        public string ServiceName { get; private set; }        

        [TikProperty("ac-name", IsMandatory = false, IsReadOnly = true)]
        public string AcName { get; private set; }

        [TikProperty("ac-mac", IsMandatory = false, IsReadOnly = true)]
        public string AcMac { get; private set; }

        [TikProperty("mtu", IsMandatory = false, IsReadOnly = true)]
        public string Mtu { get; private set; }
        
        [TikProperty("mru", IsMandatory = false, IsReadOnly = true)]
        public string Mru { get; private set; }

        [TikProperty("local-address", IsMandatory = false, IsReadOnly = true)]
        public string LocalAddress { get; private set; }

        [TikProperty("remote-address", IsMandatory = false, IsReadOnly = true)]
        public string RemoteAddress { get; private set; }
        
        /// <summary>
        /// Gets snapshot of actual values for given <paramref name="interfaceName"/>.
        /// </summary>
        public static InterfacePppoeClientMonitor GetSnapshot(ITikConnection connection, string numbers)
        {
            return connection.GetInterfacePppoeClientMonitorSnapshot(numbers);
        }
    }

    /// <summary>
    /// Connection extension class for <see cref="InterfaceMonitorTraffic"/>
    /// </summary>
    public static class InterfacePppoeClientMonitorConnectionExtensions
    {
        public static InterfacePppoeClientMonitor GetInterfacePppoeClientMonitorSnapshot(this ITikConnection connection, string numbers)
        {
            var result = connection.LoadSingle<InterfacePppoeClientMonitor>(
                connection.CreateParameter("numbers", numbers, TikCommandParameterFormat.NameValue),
                connection.CreateParameter("once", "", TikCommandParameterFormat.NameValue));

            return result;
        }
    }
}