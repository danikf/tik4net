using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace tik4net.Objects.Ip.Proxy
{
    /// <summary>
    /// /ip/proxy/access: Web proxy access control list. Rules are evaluated in order; the first match
    /// decides whether the request is allowed, denied, or redirected. Use <see cref="TikConnectionExtensions.LoadAll{T}"/>
    /// to load and <see cref="TikConnectionExtensions.Save{T}"/> / <see cref="TikConnectionExtensions.Delete{T}"/> to modify.
    /// </summary>
    [TikEntity("/ip/proxy/access", IncludeDetails = true, IsOrdered = true)]
    public class IpProxyAccess
    {
        /// <summary>.id — primary key of the rule.</summary>
        [TikProperty(".id", IsReadOnly = true, IsMandatory = true)]
        public string Id { get; private set; }

        /// <summary>action — what to do when the rule matches.
        /// <seealso cref="ProxyAccessAction"/></summary>
        [TikProperty("action", DefaultValue = "allow")]
        public ProxyAccessAction Action { get; set; }

        /// <summary>action-data — URL to redirect to when action=deny (wiki: "redirect-to"). Only used with deny action.</summary>
        [TikProperty("action-data", DefaultValue = "")]
        public string ActionData { get; set; }

        /// <summary>src-address — source IP address or range to match (e.g. 192.168.1.0/24).</summary>
        [TikProperty("src-address", DefaultValue = "")]
        public string SrcAddress { get; set; }

        /// <summary>dst-address — destination IP address or range of the target server.</summary>
        [TikProperty("dst-address", DefaultValue = "")]
        public string DstAddress { get; set; }

        /// <summary>dst-host — destination hostname or IP to match (e.g. *.example.com).</summary>
        [TikProperty("dst-host", DefaultValue = "")]
        public string DstHost { get; set; }

        /// <summary>dst-port — destination port or port range to match (e.g. 80 or 80-90).</summary>
        [TikProperty("dst-port", DefaultValue = "")]
        public string DstPort { get; set; }

        /// <summary>local-port — proxy listening port through which the request was received. 0 = not set.</summary>
        [TikProperty("local-port", DefaultValue = "0")]
        public int LocalPort { get; set; }

        /// <summary>method — HTTP request method to match.
        /// <seealso cref="ProxyHttpMethod"/></summary>
        [TikProperty("method", DefaultValue = "any")]
        public ProxyHttpMethod Method { get; set; }

        /// <summary>path — requested URL path (without server name) to match (e.g. /ads/*).</summary>
        [TikProperty("path", DefaultValue = "")]
        public string Path { get; set; }

        /// <summary>disabled — when yes, the rule is inactive.</summary>
        [TikProperty("disabled", DefaultValue = "no")]
        public bool Disabled { get; set; }

        /// <summary>comment — free-form annotation.</summary>
        [TikProperty("comment", DefaultValue = "")]
        public string Comment { get; set; }

        // --- Read-only ---

        /// <summary>hits — number of requests that matched this rule.</summary>
        [TikProperty("hits", IsReadOnly = true)]
        public int Hits { get; private set; }

        /// <summary>Human-readable rule summary.</summary>
        public override string ToString() => string.Format("{0} src={1} dst-host={2} path={3}", Action, SrcAddress, DstHost, Path);
    }

    /// <summary>Action for <see cref="IpProxyAccess.Action"/>.</summary>
    public enum ProxyAccessAction
    {
        /// <summary>allow — permit the matched request.</summary>
        [TikEnum("allow")] Allow,

        /// <summary>deny — block the matched request (optionally redirect via <see cref="IpProxyAccess.ActionData"/>).</summary>
        [TikEnum("deny")] Deny,
    }

    /// <summary>HTTP method filter for <see cref="IpProxyAccess.Method"/>.</summary>
    public enum ProxyHttpMethod
    {
        /// <summary>any — match any HTTP method.</summary>
        [TikEnum("any")] Any,

        /// <summary>connect — HTTP CONNECT (tunneling).</summary>
        [TikEnum("connect")] Connect,

        /// <summary>delete — HTTP DELETE.</summary>
        [TikEnum("delete")] Delete,

        /// <summary>get — HTTP GET.</summary>
        [TikEnum("get")] Get,

        /// <summary>head — HTTP HEAD.</summary>
        [TikEnum("head")] Head,

        /// <summary>options — HTTP OPTIONS.</summary>
        [TikEnum("options")] Options,

        /// <summary>post — HTTP POST.</summary>
        [TikEnum("post")] Post,

        /// <summary>put — HTTP PUT.</summary>
        [TikEnum("put")] Put,

        /// <summary>trace — HTTP TRACE.</summary>
        [TikEnum("trace")] Trace,
    }
}
