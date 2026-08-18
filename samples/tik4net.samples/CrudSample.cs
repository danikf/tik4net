using tik4net.Objects;
using tik4net.Objects.Ip;

namespace tik4net.Samples;

/// <summary>
/// The O/R mapper end to end — load, create, modify, delete — printing what actually goes on the wire.
/// </summary>
/// <remarks>
/// <para>
/// Neither old demo showed this, which was the gap worth closing while consolidating them: the mapper is
/// what most consumers use, and its most surprising property is invisible from the result — <b>a save
/// sends only the fields that changed.</b> The wire echo is on so the <c>/set</c> can be seen carrying one
/// field rather than forty.
/// </para>
/// <para>
/// It creates one address on a throwaway interface name and deletes it again in a <c>finally</c>. Point it
/// at a lab router, not at anything you care about.
/// </para>
/// </remarks>
public static class CrudSample
{
    private const string SampleAddress = "192.0.2.1/24";   // TEST-NET-1, never routed

    /// <summary>Runs the create/modify/delete round trip.</summary>
    public static async Task RunAsync(SampleOptions options)
    {
        using ITikConnection connection = options.Open();
        connection.OnWriteRow += (_, args) => Write(ConsoleColor.Magenta, "> " + args.Word);

        Console.WriteLine($"-- reading /ip/address over {options.Transport}");
        var existing = (await connection.LoadAllAsync<IpAddress>()).ToList();
        foreach (var address in existing)
        {
            // Spelled the router's way, not C#'s: Disabled is a bool? and its ToString() would print
            // "False"/"" — neither of which is a word RouterOS uses.
            string disabled = address.Disabled switch { true => "yes", false => "no", null => "(unset)" };
            Console.WriteLine($"   {address.Address,-20} {address.Interface,-12} disabled={disabled}");
        }

        string @interface = existing.FirstOrDefault()?.Interface
            ?? throw new InvalidOperationException("The router has no addresses, so there is no interface to borrow.");

        var created = new IpAddress
        {
            Address = SampleAddress,
            Interface = @interface,
            Comment = "tik4net sample — safe to delete",
        };

        Console.WriteLine();
        Console.WriteLine($"-- creating {SampleAddress} on {@interface}");
        await connection.SaveAsync(created);
        Console.WriteLine($"   .id = {created.Id}");

        try
        {
            // The point of the sample: only Comment is sent, because only Comment changed.
            Console.WriteLine();
            Console.WriteLine("-- changing one field (watch the /set below carry exactly one)");
            created.Comment = "tik4net sample — edited";
            await connection.SaveAsync(created);

            Console.WriteLine();
            Console.WriteLine("-- reloading by id");
            // created.Id is set by the Save above — an entity that failed to save would have thrown.
            var reloaded = await connection.LoadByIdAsync<IpAddress>(created.Id!);
            Console.WriteLine($"   comment = {reloaded.Comment}");
        }
        finally
        {
            Console.WriteLine();
            Console.WriteLine("-- deleting");
            await connection.DeleteAsync(created);
        }
    }

    private static void Write(ConsoleColor color, string text)
    {
        ConsoleColor previous = Console.ForegroundColor;
        Console.ForegroundColor = color;
        Console.WriteLine(text);
        Console.ForegroundColor = previous;
    }
}
