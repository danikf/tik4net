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
        "specific emit sites (e.g. ['wbxcli.mepty']). (includeRawTrace=true is a back-compat alias for traceLevel='words'.)")]
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

        string WithTrace(string body)
        {
            if (trace is not { Count: > 0 } && wireCollector is not { Count: > 0 })
                return body;

            var sb = new StringBuilder(body);
            if (trace is { Count: > 0 })
            {
                sb.AppendLine().AppendLine();
                sb.AppendLine($"--- RAW TRACE ({transportType}, {trace.Count} rows) ---");
                foreach (var row in trace)
                    sb.AppendLine(row);
            }
            if (wireCollector is { Count: > 0 })
            {
                sb.AppendLine().AppendLine();
                sb.AppendLine($"--- WIRE TRACE (bytes) ({transportType}, {wireCollector.Count} events) ---");
                wireCollector.AppendTo(sb);
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

    private static ITikConnection OpenConnection(
        TikConnectionSetup setup, TikConnectionType transportType, string? routerMac)
    {
        return transportType switch
        {
            TikConnectionType.Api => setup.CreateApiConnection(),
            TikConnectionType.ApiSsl => setup.CreateApiSslConnection(),
            TikConnectionType.Rest => setup.CreateRestConnection(),
            TikConnectionType.RestSsl => setup.CreateRestSslConnection(),
            TikConnectionType.Telnet => setup.CreateTelnetConnection(),
            TikConnectionType.MacTelnet => setup.CreateMacTelnetConnection(routerMac),
            TikConnectionType.WinboxCli => setup.CreateWinboxCliConnection(),
            TikConnectionType.WinboxCliMac => setup.CreateWinboxCliMacConnection(routerMac),
            TikConnectionType.WinboxNative => setup.CreateWinboxNativeConnection(),
            _ => throw new ArgumentOutOfRangeException(nameof(transportType), transportType, "Unsupported transport."),
        };
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
