using System.Collections.Generic;
using tik4net.Connection;

namespace tik4net.unittests.Connection
{
    /// <summary>
    /// Sets and reads the O/R mapper's transport hints on a command, as the mapper does (<see cref="ITikTransportHintsCommand"/>),
    /// for tests that drive a transport with a hand-built command.
    /// </summary>
    internal static class TransportHintsTestExtensions
    {
        /// <summary>The command, carrying <paramref name="hints"/>. Fails for a command that cannot carry hints.</summary>
        internal static ITikCommand WithHints(this ITikCommand command, TikTransportHints hints)
        {
            ((ITikTransportHintsCommand)command).Hints = hints;
            return command;
        }

        /// <summary>The hints the command carries.</summary>
        internal static TikTransportHints HintsOf(this ITikCommand command)
            => ((ITikTransportHintsCommand)command).Hints;

        /// <summary>Hints asking for the CLI's <c>:serialize</c> read, re-reading <paramref name="freeTextFields"/> where it is missing.</summary>
        internal static TikTransportHints Serialize(params string[] freeTextFields)
            => new TikTransportHints(false, true, freeTextFields, new List<string>(), false, null);

        /// <summary>Hints naming <paramref name="flagFields"/> for the CLI's flags read.</summary>
        internal static TikTransportHints Flags(params string[] flagFields)
            => TikTransportHints.None.WithFlagFields(flagFields);
    }
}
