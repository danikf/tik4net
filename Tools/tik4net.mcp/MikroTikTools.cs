using System.ComponentModel;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using ModelContextProtocol.Server;
using tik4net.Cli;
using tik4net.Diagnostics;

namespace tik4net.mcp;

[McpServerToolType]
public sealed class MikroTikTools
{
    private static readonly TikConnectionType[] SupportedTransports =
    {
        TikConnectionType.Api,
        TikConnectionType.ApiSsl,
        TikConnectionType.Rest,
        TikConnectionType.RestSsl,
        TikConnectionType.Telnet,
        TikConnectionType.MacTelnet,
        TikConnectionType.WinboxCli,
        TikConnectionType.WinboxCliMac,
        TikConnectionType.WinboxNative,
    };

    [McpServerTool]
    [Description(
        "Execute a MikroTik command against a router over any supported transport and return the results. " +
        "The same low-level word/sentence call (CallCommandSync) runs over every transport, so the command " +
        "and parameters format is identical regardless of transport — this is the tool for debugging/comparing " +
        "the protocol across transports. " +
        "Returns JSON array of !re records, 'OK (no data returned)' for commands with no output, or an ERROR/TRAP message. " +
        "Parameters use MikroTik API format: filter words start with '?' (e.g. '?disabled=yes'), " +
        "name-value words start with '=' (e.g. '=address=192.168.1.1/24'). " +
        "Use executeMode='nonquery' for ACTION verbs that perform work and return no rows " +
        "(e.g. /system/script/run, /system/reboot): over command transports (Telnet/MacTelnet/WinboxCli/WinboxNative) " +
        "the default 'auto' path routes such verbs to the read/print path and throws NotSupportedException, " +
        "whereas 'nonquery' invokes ExecuteNonQuery() (fire-and-forget, dispatches the action/SYS_CMD). " +
        "Set traceLevel='words' to also get the raw words exchanged with the router (per-transport wire/CLI form) " +
        "for the command exchange, or traceLevel='bytes' to additionally capture a byte/frame-level wire trace " +
        "(pre-ANSI-strip terminal bytes, mepty pull/settle decisions, M2 frame chunking, socket reads/writes) — " +
        "the layer a terminal/transport desync actually lives in. Use traceChannels to filter the byte trace to " +
        "specific emit sites (e.g. ['wbxcli.mepty']). (includeRawTrace=true is a back-compat alias for traceLevel='words'.) " +
        "Set includeRouterLog=true to also append the router's own /log lines emitted during the command (captured over " +
        "a separate API connection), giving the device-side view next to the wire trace.")]
    public string MikrotikCall(
        [Description("IP address or hostname of the MikroTik router")] string host,
        [Description("Username for authentication")] string username,
        [Description("Password for authentication")] string password,
        [Description("API command path, e.g. /ip/address/print or /system/resource/print or /ip/address/add")] string command,
        [Description("Transport to use (case-insensitive): Api (default, TCP 8728), ApiSsl (TCP 8729), " +
                     "Rest (HTTP 80), RestSsl (HTTPS 443), Telnet (TCP 23), MacTelnet (UDP 20561), " +
                     "WinboxCli (TCP 8291, encrypted terminal), WinboxCliMac (UDP 20561, encrypted terminal over MAC), " +
                     "WinboxNative (TCP 8291, structured M2). " +
                     "All transports accept the same command/parameters format. " +
                     "Telnet/MacTelnet/Winbox* do not support Listen/Streaming.")]
        string transport = "Api",
        [Description("TCP/UDP port. 0 = use the transport default")] int port = 0,
        [Description("Router MAC address 'AA:BB:CC:DD:EE:FF' — only for MacTelnet / WinboxCliMac. " +
                     "When omitted the router MAC is discovered via MNDP (up to 5 s).")]
        string? routerMac = null,
        [Description("Back-compat alias for traceLevel='words': when true, also return the raw words exchanged " +
                     "with the router for the command. Default: false. Prefer traceLevel.")]
        bool includeRawTrace = false,
        [Description("Wire-trace verbosity (case-insensitive): 'off' (default, no trace), " +
                     "'words' (the raw words/CLI lines exchanged — same as includeRawTrace=true), or " +
                     "'bytes' (words PLUS a byte/frame-level trace: pre-ANSI terminal bytes, mepty PULL/prompt/" +
                     "settle/timeout decisions, WinBox M2 frame chunks, telnet/mactelnet socket I/O, API word bytes). " +
                     "Use 'bytes' to diagnose a transport-level hang or desync.")]
        string traceLevel = "off",
        [Description("Optional filter for the byte trace (traceLevel='bytes' only): only emit sites whose channel " +
                     "id is in this list are kept. Channels: 'wbxcli.mepty', 'wbxtcp.frame', 'telnet.sock', " +
                     "'mactelnet.udp', 'api.word'. Omit to keep all channels.")]
        string[]? traceChannels = null,
        [Description("When true, also append the router's own /log lines that appeared DURING the command, as a " +
                     "'--- ROUTER LOG ---' section — the device-side story next to the wire trace. Captured over a " +
                     "SEPARATE binary-API connection (TCP 8728, never the transport under test), so it does not " +
                     "perturb a CLI/terminal session being debugged. Note: log resolution is coarse and many actions " +
                     "log nothing by default (a successful /tool/wol is silent; a bad MAC or an ipsec error is logged). " +
                     "If the side API cannot be opened it degrades to '(router log unavailable)'. Default: false.")]
        bool includeRouterLog = false,
        [Description("Max number of router-log lines to keep (includeRouterLog only), guarding against a chatty " +
                     "router flooding the window. Default 200.")]
        int routerLogTail = 200,
        [Description("Execution path (case-insensitive): 'auto' (default) runs the low-level CallCommandSync, " +
                     "which dispatches by verb and is right for print/get and CRUD (add/set/remove/enable/disable/move). " +
                     "'nonquery' forces ExecuteNonQuery() — required for ACTION verbs that yield no result set " +
                     "(e.g. /system/script/run, /system/reboot, /system/reset-configuration) over command transports, " +
                     "where 'auto' would treat the verb as a read and throw NotSupportedException. " +
                     "On success 'nonquery' returns 'OK (action executed, no data returned)'.")]
        string executeMode = "auto",
        [Description("Optional command parameters in MikroTik API sentence format. " +
                     "Filter: '?name=value'. NameValue: '=name=value'. " +
                     "Example for set: ['=.id=*1', '=disabled=yes']. " +
                     "Example for filtered print: ['?disabled=yes'].")]
        string[]? parameters = null)
    {
        if (!Enum.TryParse<TikConnectionType>(transport, ignoreCase: true, out var transportType)
            || Array.IndexOf(SupportedTransports, transportType) < 0)
        {
            return $"ERROR (argument): unknown transport '{transport}'. Supported: "
                 + string.Join(", ", SupportedTransports);
        }

        bool nonQuery;
        switch ((executeMode ?? "auto").Trim().ToLowerInvariant())
        {
            case "auto": case "": nonQuery = false; break;
            case "nonquery": case "non-query": nonQuery = true; break;
            default:
                return $"ERROR (argument): unknown executeMode '{executeMode}'. Use 'auto' or 'nonquery'.";
        }

        bool wantWords, wantBytes;
        switch ((traceLevel ?? "off").Trim().ToLowerInvariant())
        {
            case "off": case "": wantWords = includeRawTrace; wantBytes = false; break;
            case "words": wantWords = true;  wantBytes = false; break;
            case "bytes": wantWords = true;  wantBytes = true;  break;
            default:
                return $"ERROR (argument): unknown traceLevel '{traceLevel}'. Use 'off', 'words' or 'bytes'.";
        }

        var trace = wantWords ? new List<string>() : null;
        var wireCollector = wantBytes ? new WireTraceCollector(traceChannels) : null;

        // Anchor the router-log window BEFORE the command runs (over a separate API connection so it never
        // perturbs the transport under test). Remember the newest log .id; the window is everything after it.
        string? logAnchorId = includeRouterLog ? TryReadLogAnchorId(host, username, password) : null;
        string? routerLog = null;
        bool routerLogCaptured = false;

        string WithTrace(string body)
        {
            // Capture the log window exactly once, at the first exit after the command was attempted, so
            // success and error paths both see the device-side lines emitted during the call.
            if (includeRouterLog && !routerLogCaptured)
            {
                routerLogCaptured = true;
                routerLog = TryReadRouterLogWindow(host, username, password, logAnchorId, routerLogTail);
            }

            bool hasWords = trace is { Count: > 0 };
            bool hasBytes = wireCollector is { Count: > 0 };
            bool hasLog = !string.IsNullOrEmpty(routerLog);
            if (!hasWords && !hasBytes && !hasLog)
                return body;

            var sb = new StringBuilder(body);
            if (hasWords)
            {
                sb.AppendLine().AppendLine();
                sb.AppendLine($"--- RAW TRACE ({transportType}, {trace!.Count} rows) ---");
                foreach (var row in trace)
                    sb.AppendLine(row);
            }
            if (hasBytes)
            {
                sb.AppendLine().AppendLine();
                sb.AppendLine($"--- WIRE TRACE (bytes) ({transportType}, {wireCollector!.Count} events) ---");
                wireCollector.AppendTo(sb);
            }
            if (hasLog)
            {
                sb.AppendLine().AppendLine();
                sb.AppendLine("--- ROUTER LOG ---");
                sb.Append(routerLog);
            }
            return sb.ToString();
        }

        try
        {
            // Install the byte-level sink around the WHOLE call (login handshake included) so a hang during
            // connect is visible too. No-op when wantBytes is false. Process-wide sink — trace one call at a time.
            using var wireCapture = wantBytes ? TikWireTrace.Capture(wireCollector!) : null;

            var setup = new TikConnectionSetup(host, username, password);
            if (port > 0)
                setup.Port = port;

            using var connection = OpenConnection(setup, transportType, routerMac);

            connection.DebugEnabled = false;

            if (trace != null)
            {
                connection.OnWriteRow += (_, e) => trace.Add(">> " + e.Word);
                connection.OnReadRow += (_, e) => trace.Add("<< " + e.Word);
            }

            if (nonQuery)
            {
                // ExecuteNonQuery() path: action verbs (e.g. /system/script/run) and writes that return no
                // rows. Over command transports the verb is dispatched via RunNonQuery (CLI types the line;
                // WinboxNative invokes the .jg doit/SYS_CMD), which CallCommandSync's read path would reject.
                var cmd = connection.CreateCommand(command, ParseParameters(connection, parameters));
                cmd.ExecuteNonQuery();
                return WithTrace("OK (action executed, no data returned)");
            }

            var commandRows = new List<string> { command };
            if (parameters is { Length: > 0 })
                commandRows.AddRange(parameters);

            var sentences = connection.CallCommandSync(commandRows).ToList();
            return WithTrace(FormatResponse(sentences));
        }
        catch (TikConnectionLoginException ex)
        {
            return WithTrace($"ERROR (auth): {ex.Message}");
        }
        catch (System.Net.Sockets.SocketException ex)
        {
            return WithTrace($"ERROR (network): {ex.Message}");
        }
        catch (TikCommandTrapException ex)
        {
            return WithTrace($"ERROR (trap): {ex.Message}");
        }
        catch (Exception ex)
        {
            return WithTrace($"ERROR ({ex.GetType().Name}): {ex.Message}");
        }
    }

    private static readonly TikConnectionType[] CompletionTransports =
    {
        TikConnectionType.Telnet,
        TikConnectionType.WinboxCli,
        TikConnectionType.MacTelnet,
        TikConnectionType.WinboxCliMac,
    };

    [McpServerTool]
    [Description(
        "Enumerate what RouterOS would Tab-complete for a partial CLI command — the scriptable way to walk " +
        "the whole router menu tree and resolve an object's writable fields from a live router. " +
        "Drives terminal completion over a CLI transport (Telnet by default): sends the partial line + Tab, " +
        "captures the listing, then aborts the line so the session stays clean. " +
        "After a MENU PATH (e.g. '/interface ') it returns the child menus + command verbs; " +
        "after 'add ' or 'set ' in a menu (e.g. '/interface/vlan add ') it returns the SETTABLE PARAMETER NAMES " +
        "— i.e. the writable field set for that object, which is exactly what you need to generate a tik4net entity " +
        "(print only shows fields that have a value on some current row; completion shows them all). " +
        "Returns a JSON object { input, transport, tokens[], raw }. tokens is empty when the input completes to a " +
        "single unique token (RouterOS completes it inline). Only CLI terminal transports support this " +
        "(Telnet, WinboxCli, MacTelnet, WinboxCliMac) — not Api/Rest/WinboxNative.")]
    public string MikrotikCliComplete(
        [Description("IP address or hostname of the MikroTik router")] string host,
        [Description("Username for authentication")] string username,
        [Description("Password for authentication")] string password,
        [Description("Partial CLI command line to complete, EXACTLY as you would type before pressing Tab — " +
                     "include the trailing space to list the next word. " +
                     "Examples: '/interface ' (child menus+verbs), '/ip/firewall/filter add ' (settable params), " +
                     "'/system/resource ' (the singleton's verbs/fields).")]
        string input,
        [Description("CLI transport to use (default Telnet): Telnet (TCP 23), WinboxCli (TCP 8291), " +
                     "MacTelnet (UDP 20561), WinboxCliMac (UDP 20561). Api/Rest/WinboxNative are rejected — " +
                     "they have no terminal to complete on.")]
        string transport = "Telnet",
        [Description("TCP/UDP port. 0 = use the transport default")] int port = 0,
        [Description("Router MAC 'AA:BB:CC:DD:EE:FF' — only for MacTelnet / WinboxCliMac (else MNDP discovery).")]
        string? routerMac = null)
    {
        if (!Enum.TryParse<TikConnectionType>(transport, ignoreCase: true, out var transportType)
            || Array.IndexOf(CompletionTransports, transportType) < 0)
        {
            return $"ERROR (argument): '{transport}' does not support Tab-completion. Use a CLI terminal "
                 + "transport: " + string.Join(", ", CompletionTransports);
        }

        try
        {
            var setup = new TikConnectionSetup(host, username, password);
            if (port > 0)
                setup.Port = port;

            using var connection = OpenConnection(setup, transportType, routerMac);
            connection.DebugEnabled = false;

            if (connection is not ITikCliCompletion completion)
                return $"ERROR (internal): {transportType} connection does not implement ITikCliCompletion.";

            string raw = completion.CompleteCliRaw(input);
            var tokens = completion.CompleteCli(input);

            return JsonSerializer.Serialize(
                new { input, transport = transportType.ToString(), tokens, raw },
                new JsonSerializerOptions { WriteIndented = true });
        }
        catch (TikConnectionLoginException ex)
        {
            return $"ERROR (auth): {ex.Message}";
        }
        catch (System.Net.Sockets.SocketException ex)
        {
            return $"ERROR (network): {ex.Message}";
        }
        catch (Exception ex)
        {
            return $"ERROR ({ex.GetType().Name}): {ex.Message}";
        }
    }

    [McpServerTool]
    [Description(
        "Discover MikroTik routers on the local network segment via MNDP (MikroTik Neighbor Discovery " +
        "Protocol) — a passive listen for the UDP 5678 broadcast every RouterOS device sends. " +
        "NO host, credentials or IP are needed, which makes this the tool to use when you do not yet know " +
        "the router's address, when a rebuilt VM may have changed IP/MAC/identity, or when several MikroTiks " +
        "share the segment and picking the wrong one would be a coin flip. " +
        "Returns a JSON object { timeoutSeconds, count, routers[] }, each router carrying identity, ipv4, " +
        "ipv6, mac, version, platform, boardName, uptime and interfaceName — mac is what the MAC-layer " +
        "transports (MacTelnet, WinboxCliMac, WinboxNativeMac) need. " +
        "IMPORTANT: zero rows almost always means the HOST firewall is dropping the inbound UDP 5678 " +
        "broadcast, not that no router is present — it fails silently and looks identical to an empty " +
        "segment. A router on a different subnet is also invisible: MNDP does not cross a router.")]
    public string MikrotikDiscover(
        [Description("How long to listen, in seconds. 5-8 is plenty on a quiet segment — every device " +
                     "re-broadcasts within a few seconds. Clamped to 1-60. Note the library's own " +
                     "parameterless Discover() defaults to 60 s, which is far too long for interactive use.")]
        int timeoutSeconds = 6,
        [Description("Stop as soon as the first router answers instead of listening for the whole timeout. " +
                     "Faster, but it returns whichever device happened to broadcast first — do NOT use it " +
                     "when you intend to CHOOSE between the routers on the segment.")]
        bool stopWhenFirstFound = false)
    {
        if (timeoutSeconds < 1) timeoutSeconds = 1;
        if (timeoutSeconds > 60) timeoutSeconds = 60;

        try
        {
            // iso-8859-1 is what MndpHelper's own overloads use; the MNDP strings are single-byte and a
            // UTF-8 read mangles an identity containing a non-ASCII character.
            var encoding = Encoding.GetEncoding("iso-8859-1");

            var routers = tik4net.Mndp.MndpHelper
                .Discover(TimeSpan.FromSeconds(timeoutSeconds), encoding, stopWhenFirstFound)
                .Select(r => new
                {
                    identity = r.Identity,
                    ipv4 = r.IPv4?.ToString(),
                    ipv6 = string.IsNullOrEmpty(r.IPv6) ? null : r.IPv6,
                    mac = r.Mac,
                    version = r.Version,
                    platform = r.Platform,
                    boardName = r.BoardName,
                    uptime = r.Uptime.ToString(),
                    softwareId = r.SoftwareId,
                    interfaceName = r.InterfaceName,
                })
                .OrderBy(r => r.identity, StringComparer.OrdinalIgnoreCase)
                .ToArray();

            return JsonSerializer.Serialize(
                new { timeoutSeconds, count = routers.Length, routers },
                new JsonSerializerOptions { WriteIndented = true });
        }
        catch (System.Net.Sockets.SocketException ex)
        {
            // The usual one is AddressAlreadyInUse: something else already holds UDP 5678 (WinBox's own
            // neighbour list, MndpTray, a second copy of this server).
            return $"ERROR (network): {ex.Message} — UDP {5678} may already be held by another "
                 + "MNDP listener (WinBox's Neighbors tab, MndpTray), or blocked by the host firewall.";
        }
        catch (Exception ex)
        {
            return $"ERROR ({ex.GetType().Name}): {ex.Message}";
        }
    }

    // Parses MCP-format parameter rows ('=name=value' NameValue, '?name=value' Filter) into typed command
    // parameters for the ExecuteNonQuery() path — mirrors the row parsing in CallCommandSync so the two
    // execution modes accept an identical parameters format.
    private static ITikCommandParameter[] ParseParameters(ITikConnection connection, string[]? parameters)
    {
        var result = new List<ITikCommandParameter>();
        if (parameters == null)
            return result.ToArray();

        foreach (var row in parameters)
        {
            if (string.IsNullOrEmpty(row) || row.StartsWith(".tag=") || row.StartsWith(".tag ="))
                continue;

            if (row.StartsWith("?"))
            {
                string kv = row.TrimStart('?');
                if (kv.StartsWith("="))
                    kv = kv.Substring(1);
                int eq = kv.IndexOf('=');
                if (eq >= 0)
                    result.Add(connection.CreateParameter(kv.Substring(0, eq), kv.Substring(eq + 1), TikCommandParameterFormat.Filter));
            }
            else if (row.StartsWith("="))
            {
                string kv = row.Substring(1);
                int eq = kv.IndexOf('=');
                if (eq >= 0)
                    result.Add(connection.CreateParameter(kv.Substring(0, eq), kv.Substring(eq + 1), TikCommandParameterFormat.NameValue));
            }
        }

        return result.ToArray();
    }

    // One call rather than a per-transport switch: the setup applies routerMac only to the transports
    // that address the router by MAC (ITikMacLayerConnection), so a transport list cannot fall behind
    // the library's — the switch this replaced had no WinboxNativeMac case and refused it outright.
    private static ITikConnection OpenConnection(
        TikConnectionSetup setup, TikConnectionType transportType, string? routerMac)
    {
        setup.RouterMac = routerMac;
        return setup.Create(transportType);
    }

    // ── Router-log correlation (includeRouterLog) ────────────────────────────────
    // The window is captured over a dedicated binary-API connection (TCP 8728), deliberately NOT the
    // transport under test: reading /log over a wedged mepty session would just hang, and injecting print
    // commands into the very stream being traced would corrupt it (the P2.13 contamination lesson). The
    // window is an .id-diff — the newest log .id before the command, everything after it — which is exact
    // and independent of the router's log time format (which varies with /system/logging settings).

    private static ITikConnection? TryOpenSideApi(string host, string username, string password)
    {
        try
        {
            var setup = new TikConnectionSetup(host, username, password);   // API default port (8728)
            return setup.CreateApiConnection();
        }
        catch
        {
            return null;   // side channel is best-effort; caller degrades to "(router log unavailable)".
        }
    }

    // Newest log .id before the command (log prints oldest-first, newest last). .proplist=.id keeps the
    // read thin. Null when the log is empty or the side channel cannot be opened.
    private static string? TryReadLogAnchorId(string host, string username, string password)
    {
        try
        {
            using var conn = TryOpenSideApi(host, username, password);
            if (conn == null)
                return null;

            string? last = null;
            foreach (var s in conn.CallCommandSync(new[] { "/log/print", "=.proplist=.id" }))
                if (s is ITikReSentence re)
                    last = re.GetResponseFieldOrDefault(".id", last);
            return last;
        }
        catch
        {
            return null;
        }
    }

    // Reads /log after the command and returns the lines added since anchorId, oldest-first, capped at
    // tail. Falls back to the last `tail` lines when the anchor is null or has aged out of the ring buffer.
    private static string TryReadRouterLogWindow(string host, string username, string password, string? anchorId, int tail)
    {
        if (tail < 1) tail = 1;
        try
        {
            using var conn = TryOpenSideApi(host, username, password);
            if (conn == null)
                return "(router log unavailable: could not open side API connection)";

            var rows = conn.CallCommandSync(new[] { "/log/print", "=.proplist=.id,time,topics,message" })
                           .OfType<ITikReSentence>()
                           .ToList();

            int start = 0;
            if (anchorId != null)
            {
                int idx = rows.FindIndex(r => r.GetResponseFieldOrDefault(".id", "") == anchorId);
                start = idx >= 0 ? idx + 1 : Math.Max(0, rows.Count - tail);
            }

            var window = rows.Skip(start).ToList();
            if (window.Count > tail)
                window = window.Skip(window.Count - tail).ToList();

            if (window.Count == 0)
                return "(no router-log lines during the command)";

            var sb = new StringBuilder();
            foreach (var r in window)
            {
                string time = r.GetResponseFieldOrDefault("time", "");
                string topics = r.GetResponseFieldOrDefault("topics", "");
                string message = r.GetResponseFieldOrDefault("message", "");
                sb.Append(time).Append("  ").Append(topics).Append("  ").AppendLine(message);
            }
            return sb.ToString();
        }
        catch (Exception ex)
        {
            return $"(router log unavailable: {ex.Message})";
        }
    }

    private string FormatResponse(List<ITikSentence> sentences)
    {
        var records = new List<Dictionary<string, string>>();

        foreach (var sentence in sentences)
        {
            switch (sentence)
            {
                case ITikReSentence re:
                    records.Add(new Dictionary<string, string>(re.Words));
                    break;
                case ITikTrapSentence trap:
                    return $"TRAP [{trap.CategoryCode}]: {trap.Message}";
            }
        }

        if (records.Count == 0)
            return "OK (no data returned)";

        return JsonSerializer.Serialize(records, new JsonSerializerOptions { WriteIndented = true });
    }

    // Collects byte/frame-level wire events (traceLevel='bytes') installed via TikWireTrace.Capture. The
    // core calls Emit on whatever thread does the I/O (for WinBox-native that is the multiplexer reader
    // thread), so appends are locked. Timestamps are relative to the first event so cause/effect ordering
    // is readable. Rendered as: [+  12ms] channel >>/<</-- escaped-bytes  (note).
    private sealed class WireTraceCollector : ITikWireTraceSink
    {
        private readonly HashSet<string>? _channels;
        private readonly Stopwatch _sw = Stopwatch.StartNew();
        private readonly List<string> _lines = new();
        private readonly object _gate = new();

        internal WireTraceCollector(string[]? channels)
        {
            if (channels is { Length: > 0 })
                _channels = new HashSet<string>(channels, StringComparer.OrdinalIgnoreCase);
        }

        internal int Count { get { lock (_gate) return _lines.Count; } }

        public void Emit(string channel, TikWireDir dir, byte[] data, int offset, int count, string note)
        {
            if (_channels != null && !_channels.Contains(channel))
                return;

            string arrow = dir switch { TikWireDir.Send => ">>", TikWireDir.Recv => "<<", _ => "--" };
            string body = TikWireTrace.Escape(data, offset, count);
            var sb = new StringBuilder();
            sb.Append('[').Append('+').Append(_sw.ElapsedMilliseconds.ToString().PadLeft(6)).Append("ms] ");
            sb.Append(channel).Append(' ').Append(arrow);
            if (body.Length > 0)
                sb.Append(' ').Append(body);
            if (!string.IsNullOrEmpty(note))
                sb.Append("  (").Append(note).Append(')');

            string line = sb.ToString();
            lock (_gate)
                _lines.Add(line);
        }

        internal void AppendTo(StringBuilder sb)
        {
            lock (_gate)
                foreach (var line in _lines)
                    sb.AppendLine(line);
        }
    }
}
