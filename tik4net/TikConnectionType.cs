using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

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
