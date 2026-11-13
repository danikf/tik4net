using System.ComponentModel;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using ModelContextProtocol.Server;
using tik4net.Api;
using tik4net.Cli;
using tik4net.Connection;
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
        TikConnectionType.Ssh,
        TikConnectionType.MacTelnet,
        TikConnectionType.WinboxCli,
        TikConnectionType.WinboxCliMac,
        TikConnectionType.WinboxNative,
        TikConnectionType.WinboxNativeMac,
    };

    // ── Which build is answering ──────────────────────────────────────────────────
    // run-dev.ps1 stages a COPY of the build output under %TEMP% for every launch, precisely so the
    // server can be replaced while clients are connected. That makes "did my rebuild take effect?"
    // unanswerable from the repository: the running process may be any staging, from any build. So
    // every response carries the timestamp of the assemblies that produced it — the one fact that
    // settles it. A stamp older than the edit means the client is still on the previous server and
    // has to reconnect; nothing about the answer can be trusted to reflect the change until it moves.
    //
    // Two assemblies, not one, because they move independently and the interesting one is NOT the
    // one named on the tin. Nearly every change lands in tik4net.dll, and `dotnet build tik4net.sln`
    // after a library-only edit refreshes tik4net.dll in this output directory while leaving
    // tik4net.mcp.dll untouched — its own compile is up to date, so MSBuild skips it and the copy
    // keeps its old timestamp. Measured 2026-08-25: solution build, tik4net.dll 09:44:56,
    // tik4net.mcp.dll 09:44:29, and a live staging whose two files were 38 minutes apart. Reporting
    // only the wrapper therefore UNDER-reports: it reads "stale" while the answers are current, and
    // a real library change can hide behind a stamp that never moves.
    internal static string ServerBuild => BuiltDescription + StaleNote();

    private static readonly string BuiltDescription = DescribeBuild();

    // Where the build output these files were staged from lives, so a rebuild can be noticed while
    // the server runs. Set by run-dev.ps1; absent for the installed global tool, where there is no
    // source directory to be behind and the check is simply skipped.
    private static readonly string? SourceDir = Environment.GetEnvironmentVariable("TIK4NET_MCP_SOURCE_DIR");

    // Set by DevRelay for the server it runs: a rebuild is then picked up on the next call, not by a reconnect.
    private static readonly bool Relayed = Environment.GetEnvironmentVariable("TIK4NET_MCP_RELAYED") == "1";

    private static string DescribeBuild()
    {
        try
        {
            string version = typeof(MikroTikTools).Assembly.GetName().Version?.ToString() ?? "?";
            string self = SelfPath();
            string dir = self.Length > 0 ? (Path.GetDirectoryName(self) ?? string.Empty) : string.Empty;

            return $"tik4net.mcp {version} built {Built(self)}"
                 + $", tik4net.dll built {Built(Path.Combine(dir, "tik4net.dll"))}"
                 + (dir.Length > 0 ? $" ({dir})" : string.Empty);
        }
        catch (Exception ex)
        {
            // Never let self-description break a router call.
            return $"tik4net.mcp (build unknown: {ex.GetType().Name})";
        }
    }

    // A single-file publish reports an empty Location; the apphost path is the file then.
    private static string SelfPath()
    {
        string path = typeof(MikroTikTools).Assembly.Location;
        return string.IsNullOrEmpty(path) ? Environment.ProcessPath ?? string.Empty : path;
    }

    private static string Built(string path)
        => path.Length > 0 && File.Exists(path)
            ? File.GetLastWriteTime(path).ToString("yyyy-MM-dd HH:mm:ss")
            : "unknown";

    /// <summary>
    /// A warning when the repository has been built since this process was staged.
    /// </summary>
    /// <remarks>
    /// Evaluated per call rather than once, because the whole point is to catch a build that happens
    /// AFTER the server started — which is every build in a working session. Two file stats.
    /// <para>This is what makes "rebuild and the MCP is current" nearly true: it still is not (the
    /// process runs from a frozen copy and must be reconnected), but the answer now says so instead
    /// of quietly describing the previous code.</para>
    /// </remarks>
    private static string StaleNote()
    {
        if (string.IsNullOrEmpty(SourceDir)) return string.Empty;
        try
        {
            string self = SelfPath();
            string dir = self.Length > 0 ? (Path.GetDirectoryName(self) ?? string.Empty) : string.Empty;
            if (dir.Length == 0) return string.Empty;

            var behind = new List<string>();
            foreach (string file in new[] { "tik4net.dll", "tik4net.mcp.dll" })
            {
                string mine = Path.Combine(dir, file);
                string theirs = Path.Combine(SourceDir!, file);
                if (!File.Exists(mine) || !File.Exists(theirs)) continue;
                // Whole seconds: the copy preserves the timestamp exactly, so anything newer is a
                // genuine rebuild rather than filesystem resolution.
                if (File.GetLastWriteTime(theirs) > File.GetLastWriteTime(mine).AddSeconds(1))
                    behind.Add($"{file} {File.GetLastWriteTime(theirs):yyyy-MM-dd HH:mm:ss}");
            }

            return behind.Count == 0
                ? string.Empty
                : " — STALE: the repository has been rebuilt since this server was staged ("
                  + string.Join(", ", behind)
                  + (Relayed
                      ? "). This answer describes the PREVIOUS code; the dev relay switches to the new build on the next call."
                      : "). Reconnect the tik4net-mcp server; this answer describes the PREVIOUS code.");
        }
        catch
        {
            return string.Empty;   // a staleness check may never break a router call either
        }
    }

    // Appends the build stamp to a plain-text answer.
    private static string Stamp(string body)
        => body + Environment.NewLine + Environment.NewLine + "--- MCP SERVER --- " + ServerBuild;

    // ── Parameter descriptions shared by the tools ───────────────────────────────

    private const string ConnectionStringDescription =
        "The whole connection as one tik4net connection string instead of the separate connection parameters " +
        "(host, username, password, transport, port, routerMac, allowInvalidCertificate, romonAgent*) " +
        "(TikConnectionSetup.FromConnectionString): " +
        "'transport=ApiSsl;host=192.168.88.1;user=admin;password=secret;allowInvalidCertificate=true'. " +
        "ADO.NET syntax, case-insensitive keys; a value holding ';' or '=' is quoted. Keys include transport, " +
        "host, routerMac, user, password, port, the timeouts, and romon.host/romon.user/romon.password/romon.port " +
        "for a router behind a RoMON agent (host is then its RoMON id). An unknown key is refused. Without " +
        "'transport' the tool's default transport is used. Giving it together with any of those parameters " +
        "is refused. " + PasswordReferenceRule;

    // The rule for a password written as a reference (TikPassword), shared by the connection string and the separate
    // password parameters.
    private const string PasswordReferenceRule =
        "A password may name where it is instead of holding it, so that it never appears here: " +
        "'env:TIK4NET_LAB_PW' (an environment variable of this server) or 'file:<path>/TIK4NET_lab.txt' (a one-line file); " +
        "only names starting with TIK4NET_ are read. 'plain:<text>' is the text as it is. dpapi: is not read here.";

    private const string HostDescription =
        "IP address or hostname of the MikroTik router. On the MAC-layer transports a MAC address (AA:BB:CC:DD:EE:FF) may be given instead, to reach a router that has no IP address. " +
        "Through a RoMON agent (romonAgentHost set): the router's RoMON id instead (AA:BB:CC:DD:EE:FF, its /tool/romon current-id).";

    private const string UsernameDescription =
        "Username for authentication — on the router named by host (through RoMON: on the target, not the agent)";

    private const string PasswordDescription =
        "Password for authentication — on the router named by host (through RoMON: the target's). " + PasswordReferenceRule;

    private const string PortDescription = "TCP/UDP port. 0 = use the transport default";

    private const string RouterMacDescription =
        "Router MAC 'AA:BB:CC:DD:EE:FF' — only for the MAC-layer transports (MacTelnet / WinboxCliMac / WinboxNativeMac). " +
        "When omitted the router MAC is discovered via MNDP (up to 5 s).";

    private const string AllowInvalidCertificateDescription =
        "ApiSsl / RestSsl only: when true, accept a self-signed or otherwise invalid router certificate " +
        "(a RouterOS device usually presents a self-signed one). Default false — the certificate is " +
        "validated against the OS trust store. The connection stays encrypted either way, but the " +
        "router's identity is not checked, so use it for a lab or a network you trust.";

    private const string RomonAgentHostDescription =
        "RoMON: the agent to reach the router through — its IP address or hostname, or on the MAC-layer " +
        "transports its MAC (AA:BB:CC:DD:EE:FF). When set, host is the target's RoMON id and username/password are " +
        "the target's; the connection logs in to the agent and continues through it — Telnet, Ssh and MacTelnet " +
        "with /tool/romon/ssh (the target user needs the 'ssh' policy), the four Winbox* transports through the " +
        "agent's WinBox relay (the 'winbox' policy). The API and REST transports do not relay. port and routerMac " +
        "are not used — see romonAgentPort. Omit for a direct connection.";

    private const string RomonAgentUsernameDescription = "RoMON: user name on the agent (required with romonAgentHost).";

    private const string RomonAgentPasswordDescription = "RoMON: password on the agent (may be empty). " + PasswordReferenceRule;

    private const string RomonAgentPortDescription = "RoMON: the agent's port for the transport; 0 = transport default.";

    // ── mikrotik_call ─────────────────────────────────────────────────────────────

    // 1 while a byte trace runs. TikWireTrace has one process-wide sink, and the server runs calls at once: a second
    // byte trace would see the first one's bytes and, ending in the other order, leave its collector installed for
    // the life of the process. It is refused rather than queued, since a queue would hide a trace that hangs.
    private static int _byteTraceRunning;

    [McpServerTool]
    [Description(
        "Execute a MikroTik command against a router over any supported transport and return the results. " +
        "The same command and parameter format runs over every transport (the mid-level ITikCommand, which " +
        "translates it into each transport's own language), so the command " +
        "and parameters format is identical regardless of transport — this is the tool for debugging/comparing " +
        "the protocol across transports. " +
        "Returns a JSON array of records, 'OK (no data returned)' for commands with no output, or an ERROR message. " +
        "A read (print, get) answers with its rows; get answers with one row named after its value-name " +
        "(or 'ret', the whole row as one as-value string, without one). add answers [{\"ret\": \"<new .id>\"}]. " +
        "Parameters use MikroTik API format: filter words start with '?' (e.g. '?disabled=yes'), " +
        "name-value words start with '=' (e.g. '=address=192.168.1.1/24'). " +
        "Use executeMode='nonquery' for ACTION verbs that perform work and return no rows " +
        "(e.g. /system/script/run, /system/reboot): over the command transports (Telnet, Ssh, MacTelnet, the WinBox " +
        "transports) the default 'auto' path reads such a verb, and the read refuses it with NotSupportedException " +
        "before anything is sent, whereas 'nonquery' invokes ExecuteNonQuery(), which runs the command (dispatching the action/SYS_CMD) and discards any rows it prints, on every transport. " +
        "Set traceLevel='words' to also get the raw words exchanged with the router (per-transport wire/CLI form) " +
        "for the command exchange, or traceLevel='bytes' to additionally capture a byte/frame-level wire trace " +
        "(pre-ANSI-strip terminal bytes, mepty pull/settle decisions, M2 frame chunking, socket reads/writes) — " +
        "the layer a terminal/transport desync actually lives in. One byte trace runs at a time: a second one while " +
        "another runs is refused. Use traceChannels to filter the byte trace to " +
        "specific emit sites (e.g. ['wbxcli.mepty']). (includeRawTrace=true is a back-compat alias for traceLevel='words'.) " +
        "Set includeRouterLog=true to also append the router's own /log lines emitted during the command (captured over " +
        "a separate connection, never less private than the call's own), giving the device-side view next to the wire trace. " +
        "A call the client cancels cancels the running command. " +
        "Every answer ends with a '--- MCP SERVER ---' line naming the version, build timestamp and path of the " +
        "assembly that answered: this server can be replaced while a client is connected, so that line is how you " +
        "confirm a rebuild actually took effect before trusting the answer to reflect it.")]
    public async Task<string> MikrotikCall(
        [Description("API command path, e.g. /ip/address/print or /system/resource/print or /ip/address/add")] string command,
        [Description(ConnectionStringDescription)] string? connectionString = null,
        [Description(HostDescription)] string? host = null,
        [Description(UsernameDescription)] string? username = null,
        [Description(PasswordDescription)] string? password = null,
        [Description("Transport to use (case-insensitive): Api (default, TCP 8728), ApiSsl (TCP 8729), " +
                     "Rest (HTTP 80), RestSsl (HTTPS 443), Telnet (TCP 23), Ssh (TCP 22), MacTelnet (UDP 20561), " +
                     "WinboxCli (TCP 8291, encrypted terminal), WinboxCliMac (UDP 20561, encrypted terminal over MAC), " +
                     "WinboxNative (TCP 8291, structured M2), WinboxNativeMac (UDP 20561, structured M2 over MAC). " +
                     "All transports accept the same command/parameters format. " +
                     "Telnet/MacTelnet/Winbox* do not support Listen/Streaming.")]
        string? transport = null,
        [Description(PortDescription)] int port = 0,
        [Description(RouterMacDescription)] string? routerMac = null,
        [Description(AllowInvalidCertificateDescription)] bool allowInvalidCertificate = false,
        [Description("Back-compat alias for traceLevel='words': when true, also return the raw words exchanged " +
                     "with the router for the command. Default: false. Prefer traceLevel.")]
        bool includeRawTrace = false,
        [Description("Wire-trace verbosity (case-insensitive): 'off' (default, no trace), " +
                     "'words' (the raw words/CLI lines exchanged — same as includeRawTrace=true), or " +
                     "'bytes' (words PLUS a byte/frame-level trace: pre-ANSI terminal bytes, mepty PULL/prompt/" +
                     "settle/timeout decisions, WinBox M2 frame chunks, telnet/mactelnet socket I/O, API word bytes). " +
                     "Use 'bytes' to diagnose a transport-level hang or desync. The byte trace is process-wide, so " +
                     "a second 'bytes' call while one runs is refused.")]
        string traceLevel = "off",
        [Description("Optional filter for the byte trace (traceLevel='bytes' only): only emit sites whose channel " +
                     "id is in this list are kept. The list of channels is not closed; the main ones are " +
                     "'api.word', 'rest.http', 'telnet.sock', 'ssh.pty', 'mactelnet.udp', 'login', " +
                     "'wbxcli.mepty', 'wbxtcp.frame', 'wbxtcp.sock' (the socket reads under those frames — the layer " +
                     "that shows a reader waiting mid-frame), 'wbx.codec', 'wbx.session', 'wbx.romon', and the CLI " +
                     "family's 'cli.*' decisions ('cli.page', 'cli.json', 'cli.dsv', 'cli.resync', 'cli.cancel'). " +
                     "Run once unfiltered to see which a transport emits. Omit to keep all channels.")]
        string[]? traceChannels = null,
        [Description("When true, also append the router's own /log lines that appeared DURING the command, as a " +
                     "'--- ROUTER LOG ---' section — the device-side story next to the wire trace. Captured over a " +
                     "SEPARATE connection, never the session under test, and never less private than the call: the binary " +
                     "API for a call already in clear text (Api, Rest, Telnet, MacTelnet; on the given port when the transport " +
                     "is Api, else 8728), API-SSL for ApiSsl/RestSsl (with the call's certificate options; on the given port " +
                     "when the transport is ApiSsl, else 8729), and the call's own transport for Ssh and the Winbox* " +
                     "transports. So it does not perturb a CLI/terminal session being debugged and is " +
                     "not part of the wire trace; through a RoMON agent, over a second relayed session " +
                     "to the target. Note: log resolution is coarse and many actions " +
                     "log nothing by default (a successful /tool/wol is silent; a bad MAC or an ipsec error is logged). " +
                     "If the side connection cannot be opened it degrades to '(router log unavailable)'. Default: false.")]
        bool includeRouterLog = false,
        [Description("Max number of router-log lines to keep (includeRouterLog only), guarding against a chatty " +
                     "router flooding the window. Default 200.")]
        int routerLogTail = 200,
        [Description("Execution path (case-insensitive): 'auto' (default) dispatches by verb over ITikCommand: " +
                     "add through ExecuteScalar, set/unset/remove/move/enable/disable/comment through ExecuteNonQuery, " +
                     "and every other verb as a read through ExecuteList — right for print, get and the monitors. " +
                     "'nonquery' forces ExecuteNonQuery() — required for ACTION verbs that yield no result set " +
                     "(e.g. /system/script/run, /system/reboot, /system/reset-configuration) over the command transports, " +
                     "where 'auto' would read the verb and the read refuses it with NotSupportedException. " +
                     "On success 'nonquery' returns 'OK (action executed, no data returned)'; rows the command printed are discarded, as ADO.NET's ExecuteNonQuery does.")]
        string executeMode = "auto",
        [Description(RomonAgentHostDescription)] string? romonAgentHost = null,
        [Description(RomonAgentUsernameDescription)] string? romonAgentUsername = null,
        [Description(RomonAgentPasswordDescription)] string? romonAgentPassword = null,
        [Description(RomonAgentPortDescription)] int romonAgentPort = 0,
        [Description("Optional command parameters in MikroTik API sentence format. " +
                     "Filter: '?name=value'. NameValue: '=name=value'. " +
                     "Example for set: ['=.id=*1', '=disabled=yes']. " +
                     "Example for filtered print: ['?disabled=yes'].")]
        string[]? parameters = null,
        CancellationToken cancellationToken = default)
    {
        TikConnectionSetup setup;
        try
        {
            setup = ResolveSetup(connectionString, transport, TikConnectionType.Api, host, username, password, port,
                routerMac, allowInvalidCertificate, romonAgentHost, romonAgentUsername, romonAgentPassword, romonAgentPort);
        }
        catch (ArgumentException ex)
        {
            return Stamp($"ERROR (argument): {ex.Message}");
        }
        var transportType = setup.ConnectionType!.Value;
        if (Array.IndexOf(SupportedTransports, transportType) < 0)
        {
            return Stamp($"ERROR (argument): unknown transport '{transportType}'. Supported: "
                       + string.Join(", ", SupportedTransports));
        }

        string? refusal = ParseCallOptions(executeMode, traceLevel, includeRawTrace,
            out bool nonQuery, out bool wantWords, out bool wantBytes);
        if (refusal != null)
            return Stamp($"ERROR (argument): {refusal}");

        if (wantBytes && Interlocked.CompareExchange(ref _byteTraceRunning, 1, 0) != 0)
            return Stamp("ERROR (argument): another byte trace is running. The byte trace is process-wide, so two at once "
                         + "would each hold the other's bytes; run this one when it has finished, or use traceLevel='words'.");
        try
        {
            var trace = wantWords ? new List<string>() : null;
            var wireCollector = wantBytes ? new WireTraceCollector(traceChannels) : null;

            // Anchor the router-log window BEFORE the command runs (over a separate connection, TryOpenSide, so it never
            // perturbs the transport under test). Remember the newest log .id; the window is everything after it.
            string? logAnchorId = includeRouterLog ? TryReadLogAnchorId(setup) : null;

            string body;
            // The byte-level sink is installed around the WHOLE call (login handshake included) so a hang during
            // connect is visible too, and removed before the log window is read: the side connection's bytes are not
            // the command's.
            using (wantBytes ? TikWireTrace.Capture(wireCollector!) : null)
            {
                try
                {
                    body = await DispatchAsync(setup, transportType, command, parameters, nonQuery, trace, cancellationToken)
                        .ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    body = ErrorText(ex);
                }
            }

            // Read on success and on error alike, so both see the device-side lines emitted during the call.
            string? routerLog = includeRouterLog ? TryReadRouterLogWindow(setup, logAnchorId, routerLogTail) : null;
            return Stamp(WithTraceSections(body, transportType, trace, wireCollector, routerLog));
        }
        finally
        {
            if (wantBytes)
                Interlocked.Exchange(ref _byteTraceRunning, 0);
        }
    }

    // executeMode and traceLevel, or the reason they are refused.
    private static string? ParseCallOptions(string? executeMode, string? traceLevel, bool includeRawTrace,
        out bool nonQuery, out bool wantWords, out bool wantBytes)
    {
        nonQuery = wantWords = wantBytes = false;
        switch ((executeMode ?? "auto").Trim().ToLowerInvariant())
        {
            case "auto": case "": nonQuery = false; break;
            case "nonquery": case "non-query": nonQuery = true; break;
            default: return $"unknown executeMode '{executeMode}'. Use 'auto' or 'nonquery'.";
        }

        switch ((traceLevel ?? "off").Trim().ToLowerInvariant())
        {
            case "off": case "": wantWords = includeRawTrace; wantBytes = false; break;
            case "words": wantWords = true; wantBytes = false; break;
            case "bytes": wantWords = true; wantBytes = true; break;
            default: return $"unknown traceLevel '{traceLevel}'. Use 'off', 'words' or 'bytes'.";
        }
        return null;
    }

    // Mid level (ITikCommand), NOT CallCommandSync. This tool's promise is that one command and parameter format works
    // on every transport, and that is what ITikCommand gives: the API-shaped path and '?'/'=' words are translated into
    // each transport's own language. CallCommandSync is the LOW level and takes the transport's own language instead —
    // API sentence words on Api/ApiSsl, CLI text on a terminal, and nothing at all on REST or WinBox native, which have
    // no command language to write.
    //
    // Dispatching by verb here is deliberate: a client may translate, the library may not. A transport that runs
    // commands asynchronously gets the client's cancellation; on any other one the command runs to its own end.
    private static async Task<string> DispatchAsync(TikConnectionSetup setup, TikConnectionType transportType,
        string command, string[]? parameters, bool nonQuery, List<string>? trace, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using var connection = setup.Create(transportType);
        connection.DebugEnabled = false;

        if (trace != null)
        {
            connection.OnWriteRow += (_, e) => trace.Add(">> " + e.Word);
            connection.OnReadRow += (_, e) => trace.Add("<< " + e.Word);
        }

        var call = connection.CreateCommand(command, ParseParameters(parameters));
        bool cancellable = connection.Supports(TikConnectionCapability.AsyncCommands);

        if (nonQuery)
        {
            // Action verbs (e.g. /system/script/run) and writes that return no rows. Over command transports the verb
            // is dispatched via RunNonQuery (CLI types the line; WinboxNative invokes the .jg doit/SYS_CMD), which a
            // read would not run.
            if (cancellable) await call.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            else call.ExecuteNonQuery();
            return "OK (action executed, no data returned)";
        }

        string verb = TikPath.Verb(command);

        if (verb == "add")
        {
            // The API's own word for an add's answer, in the shape every other answer has: a JSON array of records.
            string newId = cancellable
                ? await call.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false)
                : call.ExecuteScalar();
            return FormatRows(new[] { new Dictionary<string, string> { [TikSpecialProperties.Ret] = newId } });
        }

        if (verb is "remove" or "set" or "unset" or "move" or "enable" or "disable" or "comment")
        {
            if (cancellable) await call.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            else call.ExecuteNonQuery();
            return "OK (no data returned)";
        }

        var rows = cancellable
            ? await call.ExecuteListAsync(cancellationToken).ConfigureAwait(false)
            : call.ExecuteList().ToList();
        return FormatRows(rows.Select(r => new Dictionary<string, string>(r.Words)));
    }

    // The body followed by the sections the call asked for: the words, the bytes, the router's log.
    private static string WithTraceSections(string body, TikConnectionType transportType, List<string>? trace,
        WireTraceCollector? wireCollector, string? routerLog)
    {
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

    // What every tool answers a failure with: one line naming the kind of failure, then the library's message.
    private static string ErrorText(Exception ex) => ex switch
    {
        TikRomonRelayException e => $"ERROR (romon: {e.Reason}): {e.Message}",
        TikRomonRelayEndedException e => $"ERROR (romon: relay ended, commandMayHaveRun={e.CommandMayHaveRun}): {e.Message}",
        TikConnectionLoginException e => $"ERROR (auth): {e.Message}",
        System.Net.Sockets.SocketException e => $"ERROR (network): {e.Message}",
        TikCommandTrapException e => $"ERROR (trap): {e.Message}",
        OperationCanceledException => "ERROR (cancelled): the client cancelled the call",
        _ => $"ERROR ({ex.GetType().Name}): {ex.Message}",
    };

    // Runs a tool whose answer carries its own serverBuild; a failure is the stamped error line.
    private static string Guarded(Func<string> body)
    {
        try
        {
            return body();
        }
        catch (Exception ex)
        {
            return Stamp(ErrorText(ex));
        }
    }

    private static readonly TikConnectionType[] CompletionTransports =
    {
        TikConnectionType.Telnet,
        TikConnectionType.Ssh,
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
        "Returns a JSON object { serverBuild, input, transport, tokens[], raw } — serverBuild names the version, " +
        "build timestamp and path of the assembly that answered, so a stale server is visible rather than assumed. " +
        "When RouterOS lists, tokens are the candidates and raw is the listing. When it completes the word inline " +
        "instead (a unique completion, or the prefix every candidate shares), tokens is empty and raw is the " +
        "completed line, e.g. '/interface/vl' -> '/interface/vlan/'; re-ask with that line to list what follows. " +
        "Both are empty when there is nothing to complete. Only CLI terminal transports support this " +
        "(Telnet, Ssh, WinboxCli, MacTelnet, WinboxCliMac) — not Api/Rest/WinboxNative*.")]
    public string MikrotikCliComplete(
        [Description("Partial CLI command line to complete, EXACTLY as you would type before pressing Tab — " +
                     "include the trailing space to list the next word. " +
                     "Examples: '/interface ' (child menus+verbs), '/ip/firewall/filter add ' (settable params), " +
                     "'/system/resource ' (the singleton's verbs/fields). RouterOS 6 completes only the space form of a " +
                     "menu path ('/ip firewall filter add ') and answers nothing after the slash form; RouterOS 7 takes both.")]
        string input,
        [Description(ConnectionStringDescription)] string? connectionString = null,
        [Description(HostDescription)] string? host = null,
        [Description(UsernameDescription)] string? username = null,
        [Description(PasswordDescription)] string? password = null,
        [Description("CLI transport to use (default Telnet): Telnet (TCP 23), Ssh (TCP 22), WinboxCli (TCP 8291), " +
                     "MacTelnet (UDP 20561), WinboxCliMac (UDP 20561). Api/Rest/WinboxNative* are rejected — " +
                     "they have no terminal to complete on.")]
        string? transport = null,
        [Description(PortDescription)] int port = 0,
        [Description(RouterMacDescription)] string? routerMac = null,
        [Description(RomonAgentHostDescription)] string? romonAgentHost = null,
        [Description(RomonAgentUsernameDescription)] string? romonAgentUsername = null,
        [Description(RomonAgentPasswordDescription)] string? romonAgentPassword = null,
        [Description(RomonAgentPortDescription)] int romonAgentPort = 0)
    {
        TikConnectionSetup setup;
        try
        {
            setup = ResolveSetup(connectionString, transport, TikConnectionType.Telnet, host, username, password, port,
                routerMac, false, romonAgentHost, romonAgentUsername, romonAgentPassword, romonAgentPort);
        }
        catch (ArgumentException ex)
        {
            return Stamp($"ERROR (argument): {ex.Message}");
        }
        var transportType = setup.ConnectionType!.Value;
        if (Array.IndexOf(CompletionTransports, transportType) < 0)
        {
            return Stamp($"ERROR (argument): '{transportType}' does not support Tab-completion. Use a CLI terminal "
                       + "transport: " + string.Join(", ", CompletionTransports));
        }

        return Guarded(() =>
        {
            using var connection = setup.Create(transportType);
            connection.DebugEnabled = false;

            if (connection is not ITikCliCompletion completion)
                return Stamp($"ERROR (internal): {transportType} connection does not implement ITikCliCompletion.");

            string raw = completion.CompleteCliRaw(input);
            var tokens = completion.CompleteCli(input);

            return JsonSerializer.Serialize(
                new { serverBuild = ServerBuild, input, transport = transportType.ToString(), tokens, raw },
                new JsonSerializerOptions { WriteIndented = true });
        });
    }

    [McpServerTool]
    [Description(
        "Discover MikroTik routers on the local network segment via MNDP (MikroTik Neighbor Discovery " +
        "Protocol) — a passive listen for the UDP 5678 broadcast every RouterOS device sends. " +
        "NO host, credentials or IP are needed, which makes this the tool to use when you do not yet know " +
        "the router's address, when a rebuilt VM may have changed IP/MAC/identity, or when several MikroTiks " +
        "share the segment and picking the wrong one would be a coin flip. " +
        "Returns a JSON object { serverBuild, timeoutSeconds, count, routers[] } — serverBuild names the version, " +
        "build timestamp and path of the assembly that answered. Each router (one per MAC) carries identity, ipv4 (null when none announced), " +
        "ipv6, mac, version, platform, boardName, uptime, softwareId (the RouterOS software id, which licensing " +
        "names the device by) and interfaceName (the router's own interface the announcement left by) — mac is what " +
        "the MAC-layer transports (MacTelnet, WinboxCliMac, WinboxNativeMac) need. " +
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
                    ipv4 = r.IPv4 == null || System.Net.IPAddress.Any.Equals(r.IPv4) ? null : r.IPv4.ToString(),   // Any = none announced
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
                new { serverBuild = ServerBuild, timeoutSeconds, count = routers.Length, routers },
                new JsonSerializerOptions { WriteIndented = true });
        }
        catch (System.Net.Sockets.SocketException ex)
        {
            // The listener shares UDP 5678 (SO_REUSEADDR), so WinBox's Neighbors tab or a second copy of this server
            // does not stop it; what does is a listener that took the port exclusively.
            return Stamp($"ERROR (network): {ex.Message} — another program may hold UDP 5678 exclusively, "
                       + "or the host firewall blocks it.");
        }
        catch (Exception ex)
        {
            return Stamp(ErrorText(ex));
        }
    }

    [McpServerTool]
    [Description(
        "Scan a router's RoMON overlay — the neighbours it can reach over RoMON, each with the RoMON id that " +
        "addresses it. This is how you get the id a relayed connection needs: mikrotik_call's romonAgentHost " +
        "names this router, and host is one of the addresses returned here. " +
        "RoMON is a layer-2 overlay MikroTik devices build between themselves, so it reaches a router with NO " +
        "IP address and one on a segment this machine cannot route to — which is what makes it different from " +
        "mikrotik_discover (MNDP, this machine's own segment only, no credentials). " +
        "Returns { serverBuild, host, transport, durationSeconds, romonEnabled, currentId, count, neighbours[] } — " +
        "currentId is THIS router's own RoMON id, and each neighbour carries address (its RoMON id), identity, " +
        "version, board, hops, cost, path, l2mtu and uptime. " +
        "An empty list with romonEnabled=false is the usual cause: RoMON has to be enabled on this router " +
        "(/tool/romon/set enabled=yes) AND on the routers to be found, and it is off by default. " +
        "A neighbour appears only while it is running: this is a live scan, not a stored table.")]
    public string MikrotikRomonDiscover(
        [Description(ConnectionStringDescription)] string? connectionString = null,
        [Description("IP address or hostname of the router to scan FROM — the one whose RoMON overlay is wanted " +
                     "(this is the router that would relay, i.e. the agent). On the MAC-layer transports its MAC " +
                     "address (AA:BB:CC:DD:EE:FF) may be given instead.")] string? host = null,
        [Description("Username for authentication on that router")] string? username = null,
        [Description("Password for authentication on that router")] string? password = null,
        [Description("Transport to use (default Api). Any tik4net transport: Api, ApiSsl, Rest, RestSsl, Telnet, " +
                     "Ssh, MacTelnet, WinboxCli, WinboxCliMac, WinboxNative, WinboxNativeMac.")]
        string? transport = null,
        [Description("How long to scan, in seconds. Clamped to 2-30; below 2 the CLI transports report nothing. " +
                     "3 is enough on a small overlay — a neighbour is reported about once a second.")]
        int durationSeconds = 3,
        [Description(PortDescription)] int port = 0,
        [Description(AllowInvalidCertificateDescription)] bool allowInvalidCertificate = false,
        [Description(RouterMacDescription)] string? routerMac = null)
    {
        TikConnectionSetup setup;
        try
        {
            setup = ResolveSetup(connectionString, transport, TikConnectionType.Api, host, username, password, port,
                routerMac, allowInvalidCertificate, null, null, null, 0);
        }
        catch (ArgumentException ex)
        {
            return Stamp($"ERROR (argument): {ex.Message}");
        }
        var transportType = setup.ConnectionType!.Value;
        if (Array.IndexOf(SupportedTransports, transportType) < 0)
        {
            return Stamp($"ERROR (argument): unknown transport '{transportType}'. Use one of: "
                       + string.Join(", ", SupportedTransports));
        }

        if (durationSeconds < 2) durationSeconds = 2;
        if (durationSeconds > 30) durationSeconds = 30;

        return Guarded(() =>
        {
            using var connection = setup.Create(transportType);
            connection.DebugEnabled = false;

            // The router's own RoMON state first: an overlay that is off answers the scan with 'RoMON not
            // running' on some transports and with nothing at all on others, and "no neighbours" reads the
            // same as "not enabled" unless it is asked.
            var romon = connection.CreateCommand("/tool/romon/print").ExecuteList()
                .Select(r => r.Words).FirstOrDefault() ?? new Dictionary<string, string>();

            // A scan, not a table: the binary API and REST repeat the whole neighbour set once a second for
            // the whole duration, the CLI transports report each one once. Keyed by RoMON id, last report wins.
            var seen = new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);
            var order = new List<string>();
            var scan = connection.CreateCommandAndParameters("/tool/romon/discover",
                TikCommandParameterFormat.NameValue,
                "duration", durationSeconds.ToString(System.Globalization.CultureInfo.InvariantCulture));
            foreach (var row in scan.ExecuteList())
            {
                string address = row.GetResponseFieldOrDefault("address", string.Empty);
                if (!seen.ContainsKey(address)) order.Add(address);
                seen[address] = new Dictionary<string, string>(row.Words);
            }

            var neighbours = order.Select(a => seen[a]).Select(n => new
                {
                    address = Field(n, "address"),
                    identity = Field(n, "identity"),
                    version = Field(n, "version"),
                    board = Field(n, "board"),
                    hops = Field(n, "hops"),
                    cost = Field(n, "cost"),
                    path = Field(n, "path"),
                    l2mtu = Field(n, "l2mtu"),
                    uptime = Field(n, "uptime"),
                })
                .ToArray();

            return JsonSerializer.Serialize(
                new
                {
                    serverBuild = ServerBuild,
                    host = setup.Address.ToString(),
                    transport = transportType.ToString(),
                    durationSeconds,
                    romonEnabled = Field(romon, "enabled"),
                    currentId = Field(romon, "current-id"),
                    count = neighbours.Length,
                    neighbours,
                },
                new JsonSerializerOptions { WriteIndented = true });
        });
    }

    // A word the router did not send is null in the JSON rather than an empty string: a field a transport
    // does not report and one the router reports as empty are not the same answer.
    private static string? Field(IReadOnlyDictionary<string, string> words, string name)
        => words.TryGetValue(name, out var value) ? value : null;

    // Parses MCP-format parameter rows ('=name=value' NameValue, '?name=value' Filter) into typed command
    // parameters, for every verb. The rules live in tik4net (TikCommandRow) rather than here: a second copy of
    // them drifts, and this is the copy that would go on silently dropping a row the other one has started
    // calling an error.
    private static ITikCommandParameter[] ParseParameters(string[]? parameters)
        => parameters == null
            ? new ITikCommandParameter[0]
            : tik4net.Connection.TikCommandRow.ParseParameters(parameters, 0).ToArray();

    // The connection from connectionString, or from the separate parameters — never both: a router or a transport
    // named twice has no rule for which one wins, so the tool refuses rather than pick. The string goes through the
    // library's own parser, so its keys and refusals are the library's; a string without 'transport' takes the
    // tool's default, as the separate parameters do. ConnectionType is always set on the result.
    private static TikConnectionSetup ResolveSetup(string? connectionString, string? transport,
        TikConnectionType defaultTransport, string? host, string? username, string? password, int port,
        string? routerMac, bool allowInvalidCertificate, string? romonAgentHost, string? romonAgentUsername,
        string? romonAgentPassword, int romonAgentPort)
    {
        if (!string.IsNullOrWhiteSpace(connectionString))
        {
            var given = new List<string>();
            if (host != null) given.Add("host");
            if (username != null) given.Add("username");
            if (password != null) given.Add("password");
            if (transport != null) given.Add("transport");
            if (port > 0) given.Add("port");
            if (routerMac != null) given.Add("routerMac");
            if (allowInvalidCertificate) given.Add("allowInvalidCertificate");
            if (romonAgentHost != null || romonAgentUsername != null || romonAgentPassword != null || romonAgentPort > 0)
                given.Add("romonAgent*");
            if (given.Count > 0)
                throw new ArgumentException("connectionString already names the connection; give "
                                            + string.Join(", ", given) + " inside it instead.");
            CheckPasswordReferences(connectionString!);
            var fromString = TikConnectionSetup.FromConnectionString(connectionString!);
            fromString.ConnectionType ??= defaultTransport;
            return fromString;
        }

        if (string.IsNullOrWhiteSpace(host) || username == null)
            throw new ArgumentException("host and username are required, or a connectionString.");
        var type = defaultTransport;
        if (transport != null && !Enum.TryParse(transport, ignoreCase: true, out type))
            throw new ArgumentException($"unknown transport '{transport}'.");

        var setup = BuildSetup(host!, username, ResolvePassword(password, "password") ?? string.Empty, port, routerMac,
            romonAgentHost, romonAgentUsername, ResolvePassword(romonAgentPassword, "romonAgentPassword"), romonAgentPort);
        setup.AllowInvalidCertificate = allowInvalidCertificate;
        setup.ConnectionType = type;
        return setup;
    }

    // ── Password references ──────────────────────────────────────────────────────
    // The library reads env:, file: and dpapi: in a connection string, because there it is configuration. Here the
    // string is written by a model, which a prompt injection can steer, and which also chooses the host the password
    // is sent to: env:GITHUB_TOKEN or file:~/.ssh/id_rsa would send that secret to a server of the injector's choosing.
    // So only names this server's operator set up for it are read: a variable, or a file, whose name starts with
    // TIK4NET_ (any directory, any extension). dpapi: is not read at all: the model could hand it any DPAPI blob this
    // account can decrypt — a browser's key, for instance — and the decrypted text would go to that host.
    private const string ReferencePrefix = "TIK4NET_";

    // Every password key of a connection string, checked before the library parses (and reads) it.
    public static void CheckPasswordReferences(string connectionString)
    {
        var builder = new System.Data.Common.DbConnectionStringBuilder { ConnectionString = connectionString };
        foreach (string key in builder.Keys)
        {
            if (key.Equals("password", StringComparison.OrdinalIgnoreCase) || key.Equals("pwd", StringComparison.OrdinalIgnoreCase)
                || key.Equals("romon.password", StringComparison.OrdinalIgnoreCase))
                CheckPasswordReference(Convert.ToString(builder[key], System.Globalization.CultureInfo.InvariantCulture) ?? "", key);
        }
    }

    public static void CheckPasswordReference(string value, string key)
    {
        switch (TikPassword.Split(value, out string rest))
        {
            case TikPassword.Prefix.Env when !rest.StartsWith(ReferencePrefix, StringComparison.OrdinalIgnoreCase):
                throw new ArgumentException($"{key}: env: reads only variables whose name starts with {ReferencePrefix} here.");
            case TikPassword.Prefix.File when !System.IO.Path.GetFileName(rest).StartsWith(ReferencePrefix, StringComparison.OrdinalIgnoreCase):
                throw new ArgumentException($"{key}: file: reads only files whose name starts with {ReferencePrefix} here.");
            case TikPassword.Prefix.Dpapi:
                throw new ArgumentException($"{key}: dpapi: is not read by this server; use env:{ReferencePrefix}… or "
                                            + $"file:…/{ReferencePrefix}….");
        }
    }

    // A separate password parameter: checked as above, read, and handed to the setup as plain text — the setup's
    // constructor reads no env: or file: itself.
    private static string? ResolvePassword(string? value, string parameter)
    {
        if (value == null)
            return null;
        CheckPasswordReference(value, parameter);
        return TikPassword.Plain(TikPassword.Resolve(value, TikPasswordSources.Environment | TikPasswordSources.File));
    }

    // The tool parameters as one TikConnectionSetup, the way a library caller writes it. routerMac goes on
    // the setup rather than through a per-transport switch: the setup applies it only to the transports that
    // address the router by MAC (ITikMacLayerConnection), so a transport list here cannot fall behind the
    // library's — the switch this replaced had no WinboxNativeMac case and refused it outright.
    //
    // Through a RoMON agent the parameters keep the library's split: host/username/password name the router
    // being queried (host becomes its RoMON id), the romonAgent* ones the router that relays. Which transports
    // relay is the library's decision too — an unsupported one comes back from Create as NotSupportedException.
    // ArgumentException means the arguments themselves are wrong and is reported as such.
    private static TikConnectionSetup BuildSetup(string host, string username, string password, int port,
        string? routerMac, string? romonAgentHost, string? romonAgentUsername, string? romonAgentPassword,
        int romonAgentPort)
    {
        if (string.IsNullOrWhiteSpace(romonAgentHost))
        {
            if (romonAgentUsername != null || romonAgentPassword != null || romonAgentPort > 0)
                throw new ArgumentException("romonAgentUsername/romonAgentPassword/romonAgentPort need romonAgentHost.");
            var direct = new TikConnectionSetup(host, username, password) { RouterMac = routerMac };
            if (port > 0)
                direct.Port = port;
            return direct;
        }

        if (romonAgentUsername == null)
            throw new ArgumentException("romonAgentUsername is required with romonAgentHost.");
        if (port > 0)
            throw new ArgumentException("port is not used through a RoMON agent — the port dialled is the agent's; "
                                        + "use romonAgentPort.");
        if (!string.IsNullOrEmpty(routerMac))
            throw new ArgumentException("routerMac is not used through a RoMON agent — give the agent's MAC as "
                                        + "romonAgentHost (MacTelnet).");

        var agent = new TikRomonAgentSetup(romonAgentHost, romonAgentUsername, romonAgentPassword ?? string.Empty);
        if (romonAgentPort > 0)
            agent.Port = romonAgentPort;
        return new TikConnectionSetup(TikRouterAddress.FromRomonId(host), username, password)
        {
            RomonAgentSetup = agent,
        };
    }

    // ── Router-log correlation (includeRouterLog) ────────────────────────────────
    // The window is captured over a dedicated side connection (TryOpenSide), deliberately NOT the session
    // under test: reading /log over a wedged mepty session would just hang, and injecting print
    // commands into the very stream being traced would corrupt it. The
    // window is an .id-diff — the newest log .id before the command, everything after it — which is exact
    // and independent of the router's log time format (which varies with /system/logging settings).

    // The side channel is a session of its own, so the one being traced is left alone, and it is never less private than
    // the call: it sends the same password (SideTransportFor). It carries the call's options — timeouts, TLS validation,
    // the router's MAC — through the call's own setup; only a transport other than the call's leaves out the call's port,
    // which belongs to the call's transport. Through a RoMON agent the host is a RoMON id the API cannot dial, and the
    // agent's log is not the target's: the side channel is then a second relayed session to the target, through the same
    // agent and agent transport.
    private static ITikConnection? TryOpenSide(TikConnectionSetup target)
    {
        try
        {
            var call = target.ConnectionType!.Value;
            if (target.RomonAgentSetup != null)
                return target.Create(call);
            var side = SideTransportFor(call);
            if (side == call)
                return target.Create(call);
            // The setup is this call's own, and nothing else reads it while the side connection opens.
            int? callPort = target.Port;
            target.Port = null;
            try { return target.Create(side); }
            finally { target.Port = callPort; }
        }
        catch
        {
            return null;   // side channel is best-effort; caller degrades to "(router log unavailable)".
        }
    }

    /// <summary>
    /// The transport the router-log side channel reads over for a call over <paramref name="call"/>: the binary API for
    /// a call that is in clear text anyway (Api, Rest, Telnet, MAC-Telnet), API-SSL for one over TLS (ApiSsl, RestSsl),
    /// and the call's own transport for SSH and the WinBox family, which are encrypted and have no TLS API beside them
    /// to fall back to without asking for another service.
    /// </summary>
    public static TikConnectionType SideTransportFor(TikConnectionType call) => call switch
    {
        TikConnectionType.Api or TikConnectionType.Rest or TikConnectionType.Telnet or TikConnectionType.MacTelnet
            => TikConnectionType.Api,
        TikConnectionType.ApiSsl or TikConnectionType.RestSsl => TikConnectionType.ApiSsl,
        _ => call,
    };

    // The /log rows, oldest first, with the named fields; the command layer translates the read for whichever
    // transport the side channel is.
    private static List<ITikReSentence> ReadLog(ITikConnection conn, params string[] fields)
        => conn.CreateCommand("/log/print").ExecuteList(fields).ToList();

    // Newest log .id before the command (log prints oldest-first, newest last). Reading only .id keeps the read
    // thin. Null when the log is empty or the side channel cannot be opened.
    private static string? TryReadLogAnchorId(TikConnectionSetup target)
    {
        try
        {
            using var conn = TryOpenSide(target);
            if (conn == null)
                return null;

            string? last = null;
            foreach (var re in ReadLog(conn, ".id"))
                // last is null until the first row arrives; the default is only used when a row somehow
                // carries no .id, in which case "keep what we had" is the intended answer.
                last = re.GetResponseFieldOrDefault(".id", last!);
            return last;
        }
        catch
        {
            return null;
        }
    }

    // Reads /log after the command and returns the lines added since anchorId, oldest-first, capped at
    // tail. Falls back to the last `tail` lines when the anchor is null or has aged out of the ring buffer.
    private static string TryReadRouterLogWindow(TikConnectionSetup target, string? anchorId, int tail)
    {
        if (tail < 1) tail = 1;
        try
        {
            using var conn = TryOpenSide(target);
            if (conn == null)
                return "(router log unavailable: could not open the side connection)";

            var rows = ReadLog(conn, ".id", "time", "topics", "message");

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

    // Records as a JSON array, or the no-data line when there are none.
    private static string FormatRows(IEnumerable<Dictionary<string, string>> rows)
    {
        var records = rows.ToList();
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

        public void Emit(string channel, TikWireDir dir, byte[]? data, int offset, int count, string? note)
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
