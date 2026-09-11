using System;
using tik4net.Cli;

namespace tik4net.unittests.Cli
{
    /// <summary>
    /// Makes a scripted CLI fake answer a whole-table read the way the router does: the rows, then the
    /// <c>#n=</c> count line that <see cref="CliCommandBuilder.BuildCountedRead"/> asks for.
    /// </summary>
    /// <remarks>
    /// For fakes whose subject is something else (the async path, verb translation, cancellation), where a
    /// read must simply succeed. The count is taken from the rows the fake is about to return, so it cannot
    /// disagree with them — tests of the count itself write the marker by hand instead.
    /// </remarks>
    internal static class CountedReadFake
    {
        internal static string Answer(string cliText, string reply)
        {
            if (!cliText.StartsWith(":local d [", StringComparison.Ordinal))
                return reply;

            int records = cliText.Contains(":serialize to=json")
                ? CliJsonParser.ParseJson(reply).Count
                : CliOutputParser.ParseAsValue(reply).Count;
            return reply + (char)10 + CliCommandBuilder.CountMarker + records + "/num";
        }
    }
}
