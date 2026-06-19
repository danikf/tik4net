namespace tik4net.Objects.Routing.Ospf
{
    /// <summary>
    /// /routing/ospf/neighbor
    ///
    /// Read-only live status table of OSPF neighbors (RouterOS 7+). Each row represents one
    /// discovered OSPF neighbor, showing addressing, interface binding, FSM state, DR/BDR
    /// election results, LSA queue depths, and adjacency uptime. The table is populated
    /// automatically by the OSPF process; entries appear when a Hello packet is received and
    /// disappear when the dead-interval expires.
    /// </summary>
    [TikEntity("/routing/ospf/neighbor", IsReadOnly = true, IncludeDetails = true)]
    public class OspfNeighbor
    {
        /// <summary>OSPF neighbor finite-state-machine states.</summary>
        public enum OspfNeighborState
        {
            /// <summary>down — no Hello packets have been received recently.</summary>
            [TikEnum("down")] Down,
            /// <summary>attempt — unicast Hello sent; awaiting response (NBMA networks only).</summary>
            [TikEnum("attempt")] Attempt,
            /// <summary>init — Hello received, but this router is not yet in the neighbor's Hello list.</summary>
            [TikEnum("init")] Init,
            /// <summary>2-way — bidirectional communication established; DR/BDR election can proceed.</summary>
            [TikEnum("2-way")] TwoWay,
            /// <summary>ex-start — negotiating the master/slave relationship before DD exchange.</summary>
            [TikEnum("ex-start")] ExStart,
            /// <summary>exchange — exchanging Database Description (DD) packets.</summary>
            [TikEnum("exchange")] Exchange,
            /// <summary>loading — requesting missing LSAs via Link State Request packets.</summary>
            [TikEnum("loading")] Loading,
            /// <summary>full — fully adjacent; link-state databases are synchronized.</summary>
            [TikEnum("full")] Full,
        }

        /// <summary>.id — primary key of row</summary>
        [TikProperty(".id", IsReadOnly = true, IsMandatory = true)]
        public string Id { get; private set; }

        /// <summary>
        /// instance — name of the OSPF instance this neighbor belongs to.
        /// </summary>
        [TikProperty("instance", IsReadOnly = true)]
        public string Instance { get; private set; }

        /// <summary>
        /// area — OSPF area this neighbor was discovered in.
        /// </summary>
        [TikProperty("area", IsReadOnly = true)]
        public string Area { get; private set; }

        /// <summary>
        /// interface — local router interface through which this neighbor is reachable.
        /// </summary>
        [TikProperty("interface", IsReadOnly = true)]
        public string Interface { get; private set; }

        /// <summary>
        /// address — IP address of the neighbor's interface (next-hop address).
        /// </summary>
        [TikProperty("address", IsReadOnly = true)]
        public string/*IP*/ Address { get; private set; }

        /// <summary>
        /// router-id — OSPF router identifier of the neighbor (dotted-decimal IPv4 notation).
        /// </summary>
        [TikProperty("router-id", IsReadOnly = true)]
        public string/*IP*/ RouterId { get; private set; }

        /// <summary>
        /// state — current OSPF FSM state of the neighbor relationship.
        /// </summary>
        /// <seealso cref="OspfNeighborState"/>
        [TikProperty("state", IsReadOnly = true)]
        public OspfNeighborState State { get; private set; }

        /// <summary>
        /// state-changes — total number of OSPF FSM state transitions for this neighbor since discovery.
        /// </summary>
        [TikProperty("state-changes", IsReadOnly = true)]
        public int StateChanges { get; private set; }

        /// <summary>
        /// priority — neighbor's router priority used in DR/BDR election on multi-access networks.
        /// A value of 0 means the router is ineligible to become DR or BDR.
        /// </summary>
        [TikProperty("priority", IsReadOnly = true)]
        public int Priority { get; private set; }

        /// <summary>
        /// dr — IP address of the Designated Router on the shared segment, as reported by this neighbor.
        /// </summary>
        [TikProperty("dr", IsReadOnly = true)]
        public string/*IP*/ Dr { get; private set; }

        /// <summary>
        /// bdr — IP address of the Backup Designated Router on the shared segment.
        /// </summary>
        [TikProperty("bdr", IsReadOnly = true)]
        public string/*IP*/ Bdr { get; private set; }

        /// <summary>
        /// ls-retransmits — number of LSAs in the retransmission queue waiting for acknowledgment.
        /// </summary>
        [TikProperty("ls-retransmits", IsReadOnly = true)]
        public int LsRetransmits { get; private set; }

        /// <summary>
        /// ls-requests — number of outstanding Link State Request packets still to be sent.
        /// </summary>
        [TikProperty("ls-requests", IsReadOnly = true)]
        public int LsRequests { get; private set; }

        /// <summary>
        /// db-summaries — number of Database Description packets still to be sent during Exchange state.
        /// </summary>
        [TikProperty("db-summaries", IsReadOnly = true)]
        public int DbSummaries { get; private set; }

        /// <summary>
        /// adjacency — uptime of the full adjacency (available only when state=Full).
        /// </summary>
        [TikProperty("adjacency", IsReadOnly = true)]
        public string/*time*/ Adjacency { get; private set; }

        /// <summary>
        /// timeout — time remaining until this neighbor is declared dead (dead-interval countdown).
        /// </summary>
        [TikProperty("timeout", IsReadOnly = true)]
        public string/*time*/ Timeout { get; private set; }

        /// <summary>
        /// dynamic — true when this neighbor entry was created dynamically by the OSPF process
        /// (as opposed to a statically configured NBMA neighbor).
        /// </summary>
        [TikProperty("dynamic", IsReadOnly = true)]
        public bool Dynamic { get; private set; }

        /// <summary>
        /// virtual — true when this is a virtual link neighbor (crossing a non-backbone area).
        /// </summary>
        [TikProperty("virtual", IsReadOnly = true)]
        public bool Virtual { get; private set; }

        /// <summary>
        /// comment — optional annotation (set via /routing/ospf/neighbor set comment=...).
        /// </summary>
        [TikProperty("comment", IsReadOnly = true)]
        public string Comment { get; private set; }

        /// <summary>Human-readable identity.</summary>
        public override string ToString() => string.Format("{0} ({1}) [{2}]", RouterId, Address, State);
    }
}
