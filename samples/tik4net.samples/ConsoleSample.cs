namespace tik4net.Samples;

/// <summary>
/// An interactive prompt for raw API sentences, echoing every word in both directions.
/// </summary>
/// <remarks>
/// This is the descendant of the old <c>tik4net.console</c> demo, and it answers one question: <b>what
/// does the router actually reply to this?</b> Everything above it — the O/R mapper, the entity
/// attributes, the query builder — is a convenience over the words printed here, so when a higher layer
/// behaves unexpectedly this is the level to drop to.
/// </remarks>
public static class ConsoleSample
{
    /// <summary>Runs the prompt until an empty command is entered.</summary>
    public static Task RunAsync(SampleOptions options)
    {
        using ITikConnection connection = options.Open();
        connection.OnReadRow += (_, args) => Write(ConsoleColor.Green, "< " + args.Word);
        connection.OnWriteRow += (_, args) => Write(ConsoleColor.Magenta, "> " + args.Word);

        Console.WriteLine($"Connected to {options.Host} over {options.Transport}.");
        Console.WriteLine("Enter one word per line — the command path first, then its parameters.");
        Console.WriteLine("A blank line sends what you typed; a blank line on an empty command quits.");
        Console.WriteLine();
        Console.WriteLine("  /ip/address/print");
        Console.WriteLine("  ?disabled=false");
        Console.WriteLine("  <blank line>");
        Console.WriteLine();

        var words = new List<string>();
        while (true)
        {
            string? line = Console.ReadLine();
            if (line is null)
                break;

            if (!string.IsNullOrWhiteSpace(line))
            {
                // '|' splits one typed line into several words, so a whole command can be pasted at once.
                words.AddRange(line.Split('|').Where(w => !string.IsNullOrEmpty(w)));
                continue;
            }

            if (words.Count == 0)
                break;

            try
            {
                var response = connection.CallCommandSync(words.ToArray());
                Console.WriteLine($"-- {response.Count()} sentence(s)");
            }
            catch (TikCommandException ex)
            {
                // A !trap is the router disagreeing with the command, not a failure of the sample.
                Write(ConsoleColor.Red, $"!! {ex.GetType().Name}: {ex.Message}");
            }

            words.Clear();
        }

        return Task.CompletedTask;
    }

    private static void Write(ConsoleColor color, string text)
    {
        ConsoleColor previous = Console.ForegroundColor;
        Console.ForegroundColor = color;
        Console.WriteLine(text);
        Console.ForegroundColor = previous;
    }
}
