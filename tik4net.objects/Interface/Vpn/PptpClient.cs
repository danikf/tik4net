using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace tik4net.Objects.Interface.Vpn
{
    /// <summary>
    /// /interface/pptp-client: PPTP (Point-to-Point Tunneling Protocol) client interface list.
    /// PPTP is a tunneling protocol integrated into common operating systems and easy to configure,
    /// but it has many known security issues — consider using a more modern VPN protocol instead.
    /// Each entry represents one outbound PPTP client tunnel connecting to a remote PPTP server.
    /// Use <see cref="TikConnectionExtensions.LoadAll{T}"/> / <see cref="TikConnectionExtensions.Save{T}"/>
    /// for CRUD operations.
    /// </summary>
    [TikEntity("/interface/pptp-client", IncludeDetails = true)]
    public class PptpClient
    {
        // ---- Primary key ----

        /// <summary>.id — primary key of row.</summary>
        [TikProperty(".id", IsReadOnly = true, IsMandatory = true)]
        public string Id { get; private set; }

        // ---- Writable properties ----

        /// <summary>
        /// name — unique interface name (mandatory).
        /// </summary>
        [TikProperty("name", IsMandatory = true)]
        public string Name { get; set; }

        /// <summary>
        /// connect-to — IP address of the remote PPTP server to connect to.
        /// </summary>
        [TikProperty("connect-to")]
        public string/*IP*/ ConnectTo { get; set; }

        /// <summary>
        /// disabled — when <c>true</c> the interface will not initiate connections.
        /// Default: yes (disabled on creation).
        /// </summary>
        [TikProperty("disabled", DefaultValue = "yes")]
        public bool Disabled { get; set; }

        /// <summary>
        /// user — username sent during authentication.
        /// </summary>
        [TikProperty("user")]
        public string User { get; set; }

        /// <summary>
        /// password — password sent during authentication.
        /// </summary>
        [TikProperty("password")]
        public string Password { get; set; }

        /// <summary>
        /// profile — PPP profile applied when the tunnel is established.
        /// Default: default-encryption
        /// </summary>
        [TikProperty("profile", DefaultValue = "default-encryption")]
        public string Profile { get; set; }

        /// <summary>
        /// allow — comma-separated list of permitted authentication methods (pap, chap, mschap1, mschap2).
        /// Default: pap,chap,mschap1,mschap2 (all methods allowed).
        /// Note: the router stores and returns this as a comma-joined string.
        /// </summary>
        [TikProperty("allow", DefaultValue = "pap,chap,mschap1,mschap2")]
        public string Allow { get; set; }

        /// <summary>
        /// add-default-route — whether to add the PPTP remote address as a default route.
        /// Default: no
        /// </summary>
        [TikProperty("add-default-route", DefaultValue = "no")]
        public bool AddDefaultRoute { get; set; }

        /// <summary>
        /// default-route-distance — administrative distance applied to the auto-created default route
        /// when <see cref="AddDefaultRoute"/> is enabled. Range: 0–255.
        /// </summary>
        // router default 1; omitted on add when left 0
        [TikProperty("default-route-distance")]
        public int DefaultRouteDistance { get; set; }

        /// <summary>
        /// dial-on-demand — when <c>true</c> the tunnel connects only when outbound traffic is generated.
        /// Default: no
        /// </summary>
        [TikProperty("dial-on-demand", DefaultValue = "no")]
        public bool DialOnDemand { get; set; }

        /// <summary>
        /// keepalive-timeout — tunnel keepalive timeout in seconds; if the remote end does not respond
        /// within this interval the tunnel is torn down.
        /// Default: 60
        /// </summary>
        // router default 60; omitted on add when left 0
        [TikProperty("keepalive-timeout")]
        public int KeepaliveTimeout { get; set; }

        /// <summary>
        /// max-mru — maximum receive unit (bytes) advertised to the peer.
        /// Default: 1450
        /// </summary>
        // router default 1450; omitted on add when left 0
        [TikProperty("max-mru")]
        public int MaxMru { get; set; }

        /// <summary>
        /// max-mtu — maximum transmit unit (bytes) for the tunnel interface.
        /// Default: 1450
        /// </summary>
        // router default 1450; omitted on add when left 0
        [TikProperty("max-mtu")]
        public int MaxMtu { get; set; }

        /// <summary>
        /// mrru — maximum received reconstructed unit (bytes); enables multilink PPP when set to a
        /// non-zero value. Wire value <c>disabled</c> disables MLPPP.
        /// Default: disabled
        /// </summary>
        [TikProperty("mrru", DefaultValue = "disabled")]
        public string Mrru { get; set; }

        /// <summary>
        /// use-peer-dns — whether to use DNS servers advertised by the remote peer.
        /// Default: no
        /// </summary>
        [TikProperty("use-peer-dns", DefaultValue = "no")]
        public bool UsePeerDns { get; set; }

        /// <summary>comment — optional free-text description of this PPTP client interface.</summary>
        [TikProperty("comment")]
        public string Comment { get; set; }

        // ---- Read-only properties ----

        /// <summary>
        /// running — <c>true</c> when the tunnel is currently established and passing traffic.
        /// </summary>
        [TikProperty("running", IsReadOnly = true)]
        public bool Running { get; private set; }

        /// <summary>Human-readable identity of the PPTP client interface.</summary>
        public override string ToString() => string.Format("{0} -> {1} (disabled={2})", Name, ConnectTo, Disabled);
    }
}
