using tik4net.Objects;
using tik4net.Objects.Tool;

namespace tik4net.Samples;

/// <summary>
/// Live traffic monitor over <c>/tool/torch</c> — a read that never ends on its own.
/// </summary>
/// <remarks>
/// The descendant of the old <c>tik4net.torch</c> demo, and the shape worth studying: rows arrive on a
/// background thread through a callback, and the caller ends the read by cancelling the loading context
/// rather than by waiting for it. Anything that streams — torch, sniffer, scan, a <c>listen</c> monitor —
/// works this way.
/// </remarks>
public static class TorchSample
{
    /// <summary>Streams torch rows until a key is pressed.</summary>
    public static Task RunAsync(SampleOptions options)
    {
        using ITikConnection connection = options.Open();

        // Not every transport can stream: the binary API can, and a terminal cannot. Asking first turns
        // an obscure mid-read failure into a sentence that says what to do about it.
        if (!connection.Supports(TikConnectionCapability.Streaming))
        {
            Console.Error.WriteLine(
                $"{options.Transport} cannot stream — try --transport Api or WinboxNative.");
            return Task.CompletedTask;
        }

        Console.WriteLine($"Torching {options.InterfaceName} on {options.Host}. Press [ENTER] to stop.");
        Console.WriteLine();

        var loadingContext = connection.LoadWithCallback<ToolTorch>(
            OnRow,
            error => Console.Error.WriteLine(error.ToString()),
            connection.CreateParameter("interface", options.InterfaceName),
            connection.CreateParameter("port", "any"),
            connection.CreateParameter("src-address", "0.0.0.0/0"),
            connection.CreateParameter("dst-address", "0.0.0.0/0"));

        Console.ReadLine();
        loadingContext.Cancel();

        return Task.CompletedTask;
    }

    private static void OnRow(ToolTorch row)
        => Console.WriteLine("{0}{1} -> {2} ({3}/{4})",
            (row.IpProtocol ?? "").PadRight(8),
            Address(row.SrcAddress, row.SrcPort),
            Address(row.DstAddress, row.DstPort),
            row.Tx, row.Rx);

    private static string Address(string? ip, string? port) => $"{ip}:{port}".PadRight(21);
}
