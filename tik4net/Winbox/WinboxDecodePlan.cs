using System;
using System.Collections.Generic;

namespace tik4net.Winbox
{
    /// <summary>
    /// The resolver's answer to "how do I turn this window's records into API rows": the key → API name map, the
    /// key → <c>.jg</c> field map, and the path's derived bools, numflag members and extra spellings. Built once per
    /// command by <see cref="WinboxFieldResolver.DecodePlan"/> and handed to <see cref="WinboxRecordCodec"/>, so every
    /// decode site (a print, a monitor window, a snapshot monitor, the callback monitor) decodes by the same recipe.
    /// </summary>
    internal sealed class WinboxDecodePlan
    {
        internal WinboxDecodePlan(WinboxFieldResolver resolver)
        {
            KeyToName = resolver.BuildKeyToApiName();
            KeyToField = resolver.BuildKeyToField();
            DerivedBools = resolver.DerivedBoolFields;
            ExtraSpellings = resolver.ExtraSpellings;
            NumFlags = resolver.BuildNumFlags();
            NotReported = resolver.NotReportedNames;
            OmittedWhen = resolver.OmittedWhen;
        }

        /// <summary>The fields left out while they hold their unset text (<see cref="WinboxFieldResolver.OmittedWhen"/>), or <c>null</c>.</summary>
        internal IReadOnlyDictionary<string, string>? OmittedWhen { get; }

        /// <summary>The API names left out of every row (<see cref="WinboxFieldResolver.NotReportedNames"/>), or <c>null</c>.</summary>
        internal ISet<string>? NotReported { get; }

        /// <summary>M2 key → API field name (<see cref="WinboxFieldResolver.BuildKeyToApiName"/>).</summary>
        internal IReadOnlyDictionary<int, string> KeyToName { get; }

        /// <summary>M2 key → <c>.jg</c> field (<see cref="WinboxFieldResolver.BuildKeyToField"/>).</summary>
        internal IReadOnlyDictionary<int, WinboxJgField> KeyToField { get; }

        /// <summary>The numflag members the path prints (<see cref="WinboxFieldResolver.BuildNumFlags"/>), or <c>null</c>.</summary>
        internal IReadOnlyDictionary<int, IReadOnlyDictionary<int, string>>? NumFlags { get; }

        /// <summary>The bools derived from another decoded field (<see cref="WinboxFieldResolver.DerivedBoolFields"/>).</summary>
        internal IReadOnlyDictionary<string, Tuple<string, string>>? DerivedBools { get; }

        /// <summary>The other spellings a field is reported under (<see cref="WinboxFieldResolver.ExtraSpellings"/>).</summary>
        internal IReadOnlyDictionary<string, string[]>? ExtraSpellings { get; }
    }
}
