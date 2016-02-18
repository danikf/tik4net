using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace tik4net.Objects.Ip.DhcpServer
{
    /// <summary>
    /// ip/dhcp-server/option :  With help of DHCP Option list, it is possible to define additional custom options for DHCP Server to advertise.
    ///        According to the DHCP protocol, a parameter is returned to the DHCP client only if it requests this parameter, specifying the respective code in DHCP request Parameter-List(code 55) attribute.If the code is not included in Parameter-List attribute, DHCP server will not send it to the DHCP client.
    /// </summary>
    [TikEntity("ip/dhcp-server/option")]
    public class DhcpServerOption
    {
        /// <summary>
        /// .id: primary key of row
        /// </summary>
        [TikProperty(".id", IsReadOnly = true, IsMandatory = true)]
        public string Id { get; private set; }

        /// <summary>
        /// code: dhcp option code. All codes are available at http://www.iana.org/assignments/bootp-dhcp-parameters
        /// </summary>
        [TikProperty("code")]
        public string/*integer:1..254*/ Code { get; set; }

        /// <summary>
        /// name: Descriptive name of the option
        /// </summary>
        [TikProperty("name", IsMandatory = true)]
        public string Name { get; set; }

        /// <summary>
        /// value
        /// Parameter's value.
        /// Starting from v6.8 available data types for options are:
        ///  0xXXXX - hex string (works also in v5)
        ///  'XXXXX' - string (works also in v5 but without ' ' around the text)
        ///  $(XXXXX) - variable (currently there are no variables for server)
        ///  '10.10.10.10' - IP address
        ///  s'10.10.10.10' - IP address converted to string
        ///  '10' - decimal number
        ///  s'10' - decimal number converted to string
        /// Now it is also possible to combine data types into one, for example:
        /// "0x01'vards'$(HOSTNAME)"
        /// For example if HOSTNAME is 'kvm', then raw value will be 0x0176617264736b766d
        /// </summary>
        [TikProperty("value")]
        public string Value { get; set; }

        /// <summary>
        /// raw-value: Read only field which shows raw dhcp option value (the format actually sent out)
        /// </summary>
        [TikProperty("raw-value")]
        public string/*HEX string */ RawValue { get; set; }
    }
}
