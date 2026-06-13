using System.ComponentModel;
using System.Text;
using System.Text.Json;
using ModelContextProtocol.Server;

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
        "Set includeRawTrace=true to also get the raw words exchanged with the router (per-transport wire/CLI form) " +
        "for the command exchange.")]
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
        [Description("When true, also return the raw words exchanged with the router for the command " +
                     "(useful for debugging the per-transport protocol). Default: false.")]
        bool includeRawTrace = false,
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

        var trace = includeRawTrace ? new List<string>() : null;

        string WithTrace(string body)
        {
            if (trace is not { Count: > 0 })
                return body;

            var sb = new StringBuilder(body);
            sb.AppendLine().AppendLine();
            sb.AppendLine($"--- RAW TRACE ({transportType}, {trace.Count} rows) ---");
            foreach (var row in trace)
                sb.AppendLine(row);
            return sb.ToString();
        }

        try
        {
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
}
