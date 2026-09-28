using System;
using System.Collections.Generic;

namespace tik4net.Cli
{
    /// <summary>
    /// What a CLI connection has learnt about its router's features, each <c>null</c> until the router has answered
    /// the question: learnt from what it accepts or refuses, never from a version string — a version assumption we
    /// cannot see failing is exactly the kind that costs 30 s a command with nothing going red. One per open: a
    /// connection reopened against another router starts from nothing.
    /// </summary>
    internal sealed class RouterFeatureSet
    {
        /// <summary>
        /// Whether <c>print as-value</c> leaves the flag fields out (RouterOS before 7.20). See
        /// <c>CliConnectionBase.AsValueOmitsFlagsAsync</c>.
        /// </summary>
        internal bool? AsValueOmitsFlags;

        /// <summary>Whether <c>print</c> takes <c>proplist=</c>. See <c>CliConnectionBase.SupportsProplistAsync</c>.</summary>
        internal bool? PrintProplist;

        /// <summary>
        /// Whether <c>:serialize to=json</c> works (RouterOS 7.13+): <c>true</c> = a JSON read has succeeded,
        /// <c>false</c> = the router refused one and the plain form worked.
        /// </summary>
        internal bool? SerializeJson;

        /// <summary>
        /// Whether <c>:serialize to=dsv</c> takes <see cref="DsvProbedSeparator"/> (RouterOS 7; see
        /// <see cref="ITikCliFieldSeparatorConnection"/>), asked by <c>CliConnectionBase.EnsureDsvSupportKnownAsync</c>.
        /// </summary>
        internal bool? Dsv;

        /// <summary>The separator <see cref="Dsv"/> was answered for: a caller may change it on an open connection.</summary>
        internal string? DsvProbedSeparator;

        /// <summary>
        /// Whether the router has <c>/console/inspect</c> (RouterOS 7; 6.49.13 answers <c>bad command name inspect</c>).
        /// Without it a menu is described by Tab completion.
        /// </summary>
        internal bool? ConsoleInspect;
    }

    /// <summary>
    /// What a CLI connection has learnt about single menus or commands of its router, each from one refusal and
    /// keyed by the command text — asked once, then remembered, so a fallback costs one wasted request per menu per
    /// connection rather than one per read. One per open, as <see cref="RouterFeatureSet"/>.
    /// </summary>
    internal sealed class RouterMenuFacts
    {
        /// <summary>Menus (print command texts) whose print refused <c>show-sensitive</c>.</summary>
        internal readonly HashSet<string> ShowSensitiveRefused = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        /// <summary>The menus whose flag read by <c>find</c> the router has already refused.</summary>
        internal readonly HashSet<string> NoFlagFind = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        /// <summary>The menus and names confirmed as flags after a flag read that named no row.</summary>
        internal readonly HashSet<string> FlagFieldExists = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        /// <summary>Print commands with no <c>as-value</c> (RouterOS 6), read as their plain table instead.</summary>
        internal readonly HashSet<string> PrintWithoutAsValue = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        /// <summary>Torch commands that refused <c>proplist=</c> (RouterOS 6), read as the plain table instead.</summary>
        internal readonly HashSet<string> TorchWithoutProplist = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        /// <summary>Monitor commands whose as-value form was refused (RouterOS 6), read as a table instead.</summary>
        internal readonly HashSet<string> MonitorWithoutAsValue = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        /// <summary>Menus that cannot be windowed.</summary>
        internal readonly HashSet<string> PagingUnavailable = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        /// <summary>Menus whose windowed print leaves <c>.id</c> out (see <c>CliCommandBuilder.BuildPagedWindowIdPerRow</c>).</summary>
        internal readonly HashSet<string> WindowPrintsNoId = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    }
}
