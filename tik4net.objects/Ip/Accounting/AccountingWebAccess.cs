using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace tik4net.Objects.Ip
{
    /// <summary>
    /// ip/accounting/web-access: The web page report make it possible to use the standard Unix/Linux tool wget to collect the traffic data and save it to a file or to use MikroTik shareware Traffic Counter to display the table. If the web report is enabled and the web page is viewed, the snapshot will be made when connection is initiated to the web page. The snapshot will be displayed on the web page. TCP protocol, used by http connections with the wget tool guarantees that none of the traffic data will be lost. The snapshot image will be made when the connection from wget is initiated. Web browsers or wget should connect to URL: http://routerIP/accounting/ip.cgi
    /// </summary>
    [TikEntity("ip/accounting/web-access", IsSingleton = true)]
    public class AccountingWebAccess
    {
        /// <summary>
        /// accessible-via-web: whether the snapshot is available via web
        /// </summary>
        [TikProperty("accessible-via-web", DefaultValue = "no")]
        public string AccessibleViaWeb { get; set; }

        /// <summary>
        /// address: IP address range that is allowed to access the snapshot
        /// </summary>
        [TikProperty("address", DefaultValue = "0.0.0.0/0")]
        public string Address { get; set; }
    }
}
