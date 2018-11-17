using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace tik4net.Objects.Interface.Ethernet
{
    /// <summary>
    /// /interface ethernet monitor: command prints out current link, rate and duplex status of an interface. 
    /// </summary>
    [TikEntity("/interface/ethernet/monitor", LoadCommand = "", LoadDefaultParameneterFormat = TikCommandParameterFormat.NameValue, IncludeDetails = false, IsReadOnly = true)]
    public class EthernetMonitor
    {
        /// <summary>
        /// name
        /// </summary>
        [TikProperty("name", IsMandatory = true, IsReadOnly = true)]
        public string Name { get; private set; }

        /// <summary>
        /// auto-negotiation
        /// Current auto negotiation status:
        ///  done - negotiation completed
        ///  incomplete - negotiation failed or not yet completed
        /// </summary>
        [TikProperty("auto-negotiation")]
        public string/*done | incomplete*/ AutoNegotiation { get; set; }

        /// <summary>
        /// default-cable-settings
        /// Default cable length setting (only applicable to NS DP83815/6 cards)
        ///  short - support short cables 
        ///  standard - support standard cables
        /// </summary>
        [TikProperty("default-cable-settings")]
        public string/*short | standard*/ DefaultCableSettings { get; set; }

        /// <summary>
        /// full-duplex: Whether transmission of data occurs in two directions simultaneously
        /// </summary>
        [TikProperty("full-duplex")]
        public bool FullDuplex { get; set; }

        /// <summary>
        /// rate: Actual data rate of the connection.
        /// </summary>
        [TikProperty("rate")]
        public string/*10Mbps | 100Mbps | 1Gbps*/ Rate { get; set; }

        /// <summary>
        /// status
        /// Current link status of an interface
        ///  link-ok - the card is connected to the network
        ///  no-link - the card is not connected to the network
        ///  unknown - the connection is not recognized (if the card does not report connection status)
        /// </summary>
        [TikProperty("status")]
        public string/*link-ok | no-link | unknown*/ Status { get; set; }

        /// <summary>
        /// tx-flow-control: Whether TX flow control is used
        /// </summary>
        [TikProperty("tx-flow-control")]
        public string TxFlowControl { get; set; }

        /// <summary>
        /// rx-flow-control: Whether RX flow control is used
        /// </summary>
        [TikProperty("rx-flow-control")]
        public string RxFlowControl { get; set; }

        /// <summary>
        /// sfp-module-present: Whether SFP module is in cage
        /// </summary>
        [TikProperty("sfp-module-present")]
        public bool SfpModulePresent { get; set; }

        /// <summary>
        /// sfp-rx-lose: 
        /// </summary>
        [TikProperty("sfp-rx-lose")]
        public bool SfpRxLose { get; set; }

        /// <summary>
        /// sfp-tx-fault: 
        /// </summary>
        [TikProperty("sfp-tx-fault")]
        public bool SfpTxFault { get; set; }

        /// <summary>
        /// sfp-connector-type: 
        /// </summary>
        [TikProperty("sfp-connector-type")]
        public string SfpConnectorType { get; set; }

        /// <summary>
        /// sfp-link-length-copper: Detected link length when copper SFP module is used
        /// </summary>
        [TikProperty("sfp-link-length-copper")]
        public string SfpLinkLengthCopper { get; set; }

        /// <summary>
        /// sfp-vendor-name: Vendor of the SFP module
        /// </summary>
        [TikProperty("sfp-vendor-name")]
        public string SfpVendorName { get; set; }

        /// <summary>
        /// sfp-vendor-part-number: SFP module part number
        /// </summary>
        [TikProperty("sfp-vendor-part-number")]
        public string SfpVendorPartNumber { get; set; }

        /// <summary>
        /// sfp-vendor-revision: SFP module revision number
        /// </summary>
        [TikProperty("sfp-vendor-revision")]
        public string SfpVendorRevision { get; set; }

        /// <summary>
        /// sfp-vendor-serial: SFP module serial number
        /// </summary>
        [TikProperty("sfp-vendor-serial")]
        public string SfpVendorSerial { get; set; }

        /// <summary>
        /// sfp-manufacturing-date: SFP module manufacturing date
        /// </summary>
        [TikProperty("sfp-manufacturing-date")]
        public string SfpManufacturingDate { get; set; }

        /// <summary>
        /// eeprom: EEPROM of an SFP module
        /// </summary>
        [TikProperty("eeprom")]
        public string Eeprom { get; set; }

        /// <summary>
        /// Gets snapshot of actual values for given <paramref name="interfaceName"/>.
        /// </summary>
        public static EthernetMonitor GetSnapshot(ITikConnection connection, string interfaceName)
        {
            return EthernetMonitorConnectionExtensions.GetEthernetMonitorSnapshot(connection, interfaceName);
        }

    }

    /// <summary>
    /// Connection extension class for <see cref="InterfaceMonitorTraffic"/>
    /// </summary>
    public static class EthernetMonitorConnectionExtensions
    {
        /// <summary>
        /// Gets snapshot of actual values for given <paramref name="interfaceName"/>.
        /// </summary>
        public static EthernetMonitor GetEthernetMonitorSnapshot(this ITikConnection connection, string interfaceName)
        {
            var result = connection.LoadSingle<EthernetMonitor>(
                connection.CreateParameter("numbers", interfaceName, TikCommandParameterFormat.NameValue),
                connection.CreateParameter("once", "", TikCommandParameterFormat.NameValue));

            return result;
        }
    }
}
