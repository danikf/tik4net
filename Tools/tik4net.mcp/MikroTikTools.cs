using System.ComponentModel;
using System.Text.Json;
using ModelContextProtocol.Server;

namespace tik4net.mcp;

[McpServerToolType]
public sealed class MikroTikTools
{
    [McpServerTool]
    [Description(
        "Execute a MikroTik API command against a router and return the results. " +
        "Returns JSON array of !re records, 'OK (no data returned)' for commands with no output, or an ERROR/TRAP message. " +
        "Parameters use MikroTik API format: filter words start with '?' (e.g. '?disabled=yes'), " +
        "name-value words start with '=' (e.g. '=address=192.168.1.1/24').")]
    public string MikrotikCall(
        [Description("IP address or hostname of the MikroTik router")] string host,
        [Description("Username for authentication")] string username,
        [Description("Password for authentication")] string password,
        [Description("API command path, e.g. /ip/address/print or /system/resource/print or /ip/address/add")] string command,
        [Description("Use SSL connection (port 8729). Default: false")] bool ssl = false,
        [Description("TCP port. 0 = use default (8728 for plain, 8729 for SSL)")] int port = 0,
        [Description("Optional command parameters in MikroTik API sentence format. " +
                     "Filter: '?name=value'. NameValue: '=name=value'. " +
                     "Example for set: ['=.id=*1', '=disabled=yes']. " +
                     "Example for filtered print: ['?disabled=yes'].")]
        string[]? parameters = null)
    {
        var connectionType = ssl ? TikConnectionType.ApiSsl : TikConnectionType.Api;

        try
        {
            using var connection = port > 0
                ? ConnectionFactory.OpenConnection(connectionType, host, port, username, password)
                : ConnectionFactory.OpenConnection(connectionType, host, username, password);

            connection.DebugEnabled = false;

            var commandRows = new List<string> { command };
            if (parameters is { Length: > 0 })
                commandRows.AddRange(parameters);

            var sentences = connection.CallCommandSync(commandRows).ToList();
            return FormatResponse(sentences);
        }
        catch (TikConnectionLoginException ex)
        {
            return $"ERROR (auth): {ex.Message}";
        }
        catch (System.Net.Sockets.SocketException ex)
        {
            return $"ERROR (network): {ex.Message}";
        }
        catch (TikCommandTrapException ex)
        {
            return $"ERROR (trap): {ex.Message}";
        }
        catch (Exception ex)
        {
            return $"ERROR ({ex.GetType().Name}): {ex.Message}";
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
}
