using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace tik4net
{
    /// <summary>
    /// Connection type used to access mikrotik router. Default is <see cref="TikConnectionType.Api"/>.
    /// </summary>
    public enum TikConnectionType
    {
        /// <summary>
        /// Mikrotik API connection - default value.
        /// </summary>
        Api,
        /// <summary>
        /// Mikrotik API-SSL connection (supports only mode with certificate on mikrotik). See https://github.com/danikf/tik4net/wiki/SSL-connection for details.
        /// </summary>
        ApiSsl,
        /// <summary>
        /// MikroTik REST API over HTTP (port 80). Requires RouterOS 7.1+. No Listen/Streaming support.
        /// </summary>
        Rest,
        /// <summary>
        /// MikroTik REST API over HTTPS (port 443). Requires RouterOS 7.1+ with www-ssl service and a certificate.
        /// </summary>
        RestSsl,
        /// <summary>
        /// Mikrotik API connection for then RouterOS version 6.43 and newer.
        /// </summary>
        [Obsolete("Use 'Api' version - works for both old and new version of the login", true)]
        Api_v2,
        /// <summary>
        /// Mikrotik API-SSL connection for then RouterOS version 6.43 and newer. (supports only mode with certificate on mikrotik). See https://github.com/danikf/tik4net/wiki/SSL-connection for details. 
        /// </summary>
        [Obsolete("Use 'Api' version - works for both old and new version of the login", true)]
        ApiSsl_v2,
        /// <summary>
        /// SSH connection - NOT IMPLEMENTED YET.
        /// </summary>
        [Obsolete("For future use.", true)]        
        Ssh,
        /// <summary>
        /// Telnet connection - NOT IMPLEMENTED YET.
        /// </summary>
        [Obsolete("For future use.", true)]
        Telnet
    }
}
