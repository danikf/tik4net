using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace tik4net.Objects.Interface
{
    /// <summary>
    /// /interface/monitor-traffic
    /// NOTE: use <see cref="InterfaceMonitorTraffic.GetSnapshot"/> or with some kind of bulk/async load
    /// </summary>
    [TikEntity("/interface/monitor-traffic", LoadCommand ="", LoadDefaultParameneterFormat = TikCommandParameterFormat.NameValue, IncludeDetails = false, IsReadOnly = true)]
    public class InterfaceMonitorTraffic
    {
        /// <summary>
        /// name
        /// </summary>
        [TikProperty("name", IsMandatory = true, IsReadOnly = true)]
        public string Name { get; private set; }

        /// <summary>
        /// rx-packets-per-second
        /// </summary>
        [TikProperty("rx-packets-per-second", IsMandatory = true, IsReadOnly = true)]
        public string RxPacketsPerSecond { get; private set; }

        /// <summary>
        /// rx-bits-per-second
        /// </summary>
        [TikProperty("rx-bits-per-second", IsMandatory = true, IsReadOnly = true)]
        public string RxBitsPerSecond { get; private set; }

        /// <summary>
        /// rx-drops-per-second
        /// REMARKS: not available in all versions
        /// </summary>
        [TikProperty("rx-drops-per-second", IsMandatory = false, IsReadOnly = true)]
        public string RxDropsPerSecond { get; private set; }

        /// <summary>
        /// rx-errors-per-second
        /// REMARKS: not available in all versions
        /// </summary>
        [TikProperty("rx-errors-per-second", IsMandatory = false, IsReadOnly = true)]
        public string RxErrorsPerSecond { get; private set; }        

        /// <summary>
        /// tx-packets-per-second
        /// </summary>
        [TikProperty("tx-packets-per-second", IsMandatory = true, IsReadOnly = true)]
        public string TxPacketsPerSecond { get; private set; }

        /// <summary>
        /// tx-bits-per-second
        /// </summary>
        [TikProperty("tx-bits-per-second", IsMandatory = true, IsReadOnly = true)]
        public string TxBitsPerSecond { get; private set; }

        /// <summary>
        /// tx-drops-per-second
        /// REMARKS: not available in all versions
        /// </summary>
        [TikProperty("tx-drops-per-second", IsMandatory = false, IsReadOnly = true)]
        public string TxDropsPerSecond { get; private set; }

        /// <summary>
        /// tx-errors-per-second
        /// REMARKS: not available in all versions
        /// </summary>
        [TikProperty("tx-errors-per-second", IsMandatory = false, IsReadOnly = true)]
        public string TxErrorsPerSecond { get; private set; }

        /// <summary>
        /// tx-queue-drops-per-second
        /// </summary>
        [TikProperty("tx-queue-drops-per-second", IsMandatory = false, IsReadOnly = true)]
        public string TxQueueDropsPerSecond { get; private set; }

        /// <summary>
        /// Gets snapshot of actual values for given <paramref name="interfaceName"/>.
        /// </summary>
        public static InterfaceMonitorTraffic GetSnapshot(ITikConnection connection, string interfaceName)
        {
            return InterfaceMonitorConnectionExtensions.GetInterfaceMonitorTrafficSnapshot(connection, interfaceName);
        }
    }

    /// <summary>
    /// Connection extension class for <see cref="InterfaceMonitorTraffic"/>
    /// </summary>
    public static class InterfaceMonitorConnectionExtensions
    {
        /// <summary>
        /// Gets snapshot of actual traffic RX/TX values for given <paramref name="interfaceName"/>.
        /// </summary>
        public static InterfaceMonitorTraffic GetInterfaceMonitorTrafficSnapshot(this ITikConnection connection, string interfaceName)
        {
            var result = connection.LoadSingle<InterfaceMonitorTraffic>(
                connection.CreateParameter("interface", interfaceName, TikCommandParameterFormat.NameValue),
                connection.CreateParameter("once", "", TikCommandParameterFormat.NameValue));

            return result;
        }
    }
}
