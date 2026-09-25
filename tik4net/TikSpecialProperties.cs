using System;
using System.Collections.Generic;
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
        /// Proplist property = .proplist
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

        /// <summary>
        /// CLI-only marker — when present on a command, the CLI layer performs two
        /// print queries (detail + stats) and merges the results by .id.
        /// API and REST transports silently ignore this parameter.
        /// </summary>
        public const string CliStats = ".cli-stats";

        /// <summary>
        /// CLI-only marker — when present on a command, the CLI layer reads the result through
        /// <c>:serialize to=json</c> instead of the default <c>as-value</c> form.
        /// <para>
        /// Needed by entities carrying free-form text (see <c>TikPropertyAttribute.IsFreeText</c>):
        /// <c>as-value</c> has no escaping whatsoever, so a value containing the format's own
        /// separators (<c>;</c>, <c>=</c>, newlines) — a file body, a script source — is
        /// indistinguishable from further fields and records, and silently shreds the result.
        /// JSON is escaped, so the same read is exact.
        /// </para>
        /// <para>
        /// Requires RouterOS 7.13+ (where <c>:serialize</c> was introduced). Older routers reject the
        /// command; the CLI transports detect that once per connection and fall back to <c>as-value</c>.
        /// API, REST and WinBox-native transports silently ignore this parameter.
        /// </para>
        /// </summary>
        public const string CliJson = ".cli-json";

        /// <summary>
        /// CLI-only marker — the read adds <c>show-sensitive</c> to its <c>print</c>. RouterOS 7 leaves secrets
        /// (<c>/radius</c> <c>secret</c>, wireless keys, …) out of a terminal <c>print</c> unless asked, while
        /// the binary API and REST return them; with the marker the CLI transports read the same values.
        /// <para>
        /// Added by the O/R mapper for every entity with a sensitive property (see
        /// <c>TikPropertyAttribute.IsSensitive</c>). RouterOS 6 has no such word and prints secrets without it
        /// ("expected end of command"): a menu that refuses it is read without it, and remembered per connection.
        /// API, REST and WinBox-native transports silently ignore this parameter.
        /// </para>
        /// </summary>
        public const string CliSensitive = ".cli-sensitive";
    }
}
