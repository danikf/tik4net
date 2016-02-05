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
