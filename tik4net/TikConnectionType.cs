using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace tik4net
{
    public enum TikConnectionType
    {
        Api,
        [Obsolete("For future use.", true)]        
        Ssh,
        [Obsolete("For future use.", true)]
        Telnet
    }
}
