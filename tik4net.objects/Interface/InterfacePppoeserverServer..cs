using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace tik4net.Objects.Interface
{
    /// <summary>
    /// /interface
    /// </summary>
    [TikEntity("interface/pppoe-server/server", IncludeDetails = true)]
    public class InterfacePppoeserverServer
    {
        /// <summary>
        /// .id
        /// </summary>
        [TikProperty(".id", IsReadOnly = true, IsMandatory = true)]
        public string Id { get; private set; }

        /// <summary>
        /// service-name
        /// </summary>
        [TikProperty("service-name", IsMandatory = true)]
        public string ServiceName { get; set; }

        /// <summary>
        /// interface
        /// </summary>
        [TikProperty("interface", IsMandatory = true)]
        public string Iface { get; set; }

        /// <summary>
        /// max-mtu
        /// </summary>
        [TikProperty("max-mtu")]
        public string MaxMtu { get; set; }

        /// <summary>
        /// max-mtu
        /// </summary>
        [TikProperty("max-mtu")]
        public string MaxMru { get; set; }

        /// <summary>
        /// mrru
        /// </summary>
        [TikProperty("mrru")]
        public string Mrru { get; set; }

        /// <summary>
        /// authentication
        /// </summary>
        [TikProperty("authentication", IsMandatory = true, DefaultValue = "pap,chap,mschap1,mschap2")]
        public string Authentication { get; set; }

        /// <summary>
        /// keepalive-timeout
        /// </summary>
        [TikProperty("keepalive-timeout",DefaultValue ="10")]
        public string KeepaliveTimeout { get; set; }

        /// <summary>
        /// one-session-per-host
        /// </summary>
        [TikProperty("one-session-per-host")]
        public bool OneSessionPerHost { get; set; }

        /// <summary>
        /// max-sessions
        /// </summary>
        [TikProperty("max-sessions")]
        public string MaxSessions { get; set; }

        /// <summary>
        /// pado-delay
        /// </summary>
        [TikProperty("pado-delay")]
        public string PadoDelay { get; set; }

        /// <summary>
        /// default-profile
        /// </summary>
        [TikProperty("default-profile", IsMandatory = true, DefaultValue = "default")]
        public string DefaultProfile { get; set; }

        /// <summary>
        /// running
        /// </summary>
        [TikProperty("invalid", IsReadOnly = true)]
        public bool Running { get; private set; }

        /// <summary>
        /// disabled
        /// </summary>
        [TikProperty("disabled")]
        public bool Disabled { get; set; }
    }

}
