namespace tik4net.Samples;

/// <summary>
/// Entry point and argument parsing for the tik4net sample app.
/// </summary>
/// <remarks>
/// Router coordinates come from the command line (or the <c>TIK4NET_*</c> environment variables), never
/// from a config file checked into the repository — the old demos each shipped an App.config with a lab
/// address in it, which is both a thing to leak and a thing to forget to change.
/// </remarks>
public static class Program
{
    public static async Task<int> Main(string[] args)
    {
        if (args.Length == 0 || args[0] is "-h" or "--help" or "help")
        {
            PrintUsage();
            return args.Length == 0 ? 1 : 0;
        }

        string command = args[0];
        var options = SampleOptions.Parse(args.Skip(1));

        if (options.Host is null)
        {
            Console.Error.WriteLine("No router address. Pass --host, or set TIK4NET_HOST.");
            return 1;
        }

        try
        {
            switch (command)
            {
                case "console":
                    await ConsoleSample.RunAsync(options);
                    return 0;

                case "torch":
                    await TorchSample.RunAsync(options);
                    return 0;

                case "crud":
                    await CrudSample.RunAsync(options);
                    return 0;

                default:
                    Console.Error.WriteLine($"Unknown command '{command}'.");
                    PrintUsage();
                    return 1;
            }
        }
        catch (TikConnectionLoginException ex)
        {
            Console.Error.WriteLine($"Login refused: {ex.Message}");
            return 2;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"{ex.GetType().Name}: {ex.Message}");
            return 3;
        }
    }

    private static void PrintUsage()
    {
        Console.WriteLine("""
            tik4net samples

            usage: tik4net.samples <command> --host <address> [options]

            commands:
              console   Type raw API sentences and watch every word go out and come back.
                        The tool for answering "what does the router actually reply to this?"
              torch     Live traffic monitor (/tool/torch) — a streaming read that never ends
                        until you cancel it.
              crud      Load, create, modify and delete an /ip/address row through the O/R
                        mapper, printing the commands the mapper decided to send.

            options:
              --host <address>      router IP or hostname          (env TIK4NET_HOST)
              --user <name>         login user, default 'admin'    (env TIK4NET_USER)
              --pass <password>     login password, default empty  (env TIK4NET_PASS)
              --transport <type>    Api (default), ApiSsl, Rest, Telnet, WinboxNative, …
              --interface <name>    torch only, default 'ether1'

            examples:
              tik4net.samples console --host 192.168.88.1 --user admin
              tik4net.samples torch   --host 192.168.88.1 --interface ether1
              tik4net.samples crud    --host 192.168.88.1 --transport Rest
            """);
    }
}

/// <summary>Connection coordinates and per-sample options, from the command line or the environment.</summary>
public sealed class SampleOptions
{
    /// <summary>Router IP or hostname.</summary>
    public string? Host { get; private set; } = Environment.GetEnvironmentVariable("TIK4NET_HOST");

    /// <summary>Login user.</summary>
    public string User { get; private set; } = Environment.GetEnvironmentVariable("TIK4NET_USER") ?? "admin";

    /// <summary>Login password.</summary>
    public string Password { get; private set; } = Environment.GetEnvironmentVariable("TIK4NET_PASS") ?? "";

    /// <summary>Transport to open.</summary>
    public TikConnectionType Transport { get; private set; } = TikConnectionType.Api;

    /// <summary>Interface name, for the torch sample.</summary>
    public string InterfaceName { get; private set; } = "ether1";

    /// <summary>Parses the options following the command name.</summary>
    public static SampleOptions Parse(IEnumerable<string> args)
    {
        var options = new SampleOptions();
        string? pending = null;

        foreach (string arg in args)
        {
            if (pending is null)
            {
                pending = arg;
                continue;
            }

            switch (pending)
            {
                case "--host": options.Host = arg; break;
                case "--user": options.User = arg; break;
                case "--pass": options.Password = arg; break;
                case "--interface": options.InterfaceName = arg; break;
                case "--transport":
                    if (!Enum.TryParse<TikConnectionType>(arg, ignoreCase: true, out var transport))
                        throw new ArgumentException($"Unknown transport '{arg}'.");
                    options.Transport = transport;
                    break;
                default:
                    throw new ArgumentException($"Unknown option '{pending}'.");
            }
            pending = null;
        }

        if (pending is not null)
            throw new ArgumentException($"Option '{pending}' has no value.");

        return options;
    }

    /// <summary>Opens the connection these options describe.</summary>
    /// <remarks>
    /// The transport comes from a command-line switch, so this is the runtime-chosen case and
    /// <c>Create(TikConnectionType)</c> is the right route — there is no type to name at compile time.
    /// When the transport IS known while writing the code, prefer the per-transport factory in that
    /// transport's namespace (<c>using tik4net.Api;</c> then <c>setup.CreateApiConnection()</c>), as
    /// tik4net.examples does.
    /// <para>
    /// <see cref="TikConnectionSetup"/> rather than <c>ConnectionFactory</c>: the factory hands out a
    /// connection with transport defaults and nowhere to state an option, so a timeout or a certificate
    /// policy would have to be set on the connection object afterwards.
    /// </para>
    /// </remarks>
    public ITikConnection Open()
    {
        var setup = new TikConnectionSetup(TikRouterAddress.FromHost(Host!), User, Password);
        return setup.Create(Transport);
    }
}
