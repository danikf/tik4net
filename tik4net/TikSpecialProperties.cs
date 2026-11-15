using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text;

namespace tik4net
{
    /// <summary>
    /// Names of well known properties (set of constants)
    /// </summary>
    public static class TikSpecialProperties
    {
        /// <summary>
        /// Id property = .id
        /// </summary>
        public const string Id = ".id";

        /// <summary>
        /// Proplist property = .proplist: the fields a read returns. A parameter of this name is always sent as the field
        /// list, whatever its <see cref="TikCommandParameterFormat"/>, and every transport honours it.
        /// </summary>
        public const string Proplist = ".proplist";

        /// <summary>
        /// .tag property - used to correlate simultaneous command responses.
        /// </summary>
        public const string Tag = ".tag";

        /// <summary>
        /// value-name property used to set name of unset field in unset command
        /// </summary>
        public const string UnsetValueName = "value-name";

        /// <summary>
        /// Return value from =done sentence. See <see cref="ITikDoneSentence"/>
        /// </summary>
        public const string Ret = "ret";

        private const string NoMarker = "No longer read by any transport: the O/R mapper hands tik4net's own transports "
            + "what it takes from the entity's attributes beside the command (IncludeCliStats, IsFreeText, IsSensitive, "
            + "the flag fields), and a parameter of this name is sent to the router as a word. Remove it; there is no "
            + "replacement to call.";

        /// <summary>
        /// No longer a marker. The O/R mapper asks the CLI transports for the two-query stats merge itself, from
        /// <c>TikEntityAttribute.IncludeCliStats</c>; a parameter of this name is sent to the router as a word.
        /// </summary>
        [Obsolete(NoMarker, error: true)]
        [EditorBrowsable(EditorBrowsableState.Never)]
        public const string CliStats = ".cli-stats";

        /// <summary>
        /// No longer a marker. The O/R mapper names an entity's flag fields to the CLI transports itself; a parameter of
        /// this name is sent to the router as a word.
        /// </summary>
        [Obsolete(NoMarker, error: true)]
        [EditorBrowsable(EditorBrowsableState.Never)]
        public const string CliFlags = ".cli-flags";

        /// <summary>
        /// No longer a marker. The O/R mapper asks the CLI transports for the <c>:serialize</c> read itself, from
        /// <c>TikPropertyAttribute.IsFreeText</c>; a parameter of this name is sent to the router as a word.
        /// </summary>
        [Obsolete(NoMarker, error: true)]
        [EditorBrowsable(EditorBrowsableState.Never)]
        public const string CliJson = ".cli-json";

        /// <summary>
        /// No longer a marker. The O/R mapper asks the CLI transports for <c>show-sensitive</c> itself, from
        /// <c>TikPropertyAttribute.IsSensitive</c>; a parameter of this name is sent to the router as a word.
        /// </summary>
        [Obsolete(NoMarker, error: true)]
        [EditorBrowsable(EditorBrowsableState.Never)]
        public const string CliSensitive = ".cli-sensitive";
    }
}
