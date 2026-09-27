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
        /// CLI-only marker — the entity's flag fields (<c>disabled</c>, <c>dynamic</c>, <c>running</c>, …) as a
        /// comma-separated list. RouterOS before 7.20 leaves every flag out of <c>print as-value</c> (7.20
        /// changelog: "include flags by default when printing to value"), so on such a router the CLI layer reads
        /// the named fields with a second <c>print … as-value proplist=…</c> of the same rows and merges it by
        /// <c>.id</c>. Only names the menu knows are asked for — one unknown name refuses the whole read.
        /// <para>
        /// Added by the O/R mapper for every entity with flag properties. A read without it gets what the router
        /// prints, so a low-level print on a pre-7.20 router has no flag fields. API, REST and WinBox-native
        /// transports silently ignore this parameter.
        /// </para>
        /// </summary>
        public const string CliFlags = ".cli-flags";

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
        /// command; the CLI transports detect that once per connection and fall back to <c>as-value</c>,
        /// then re-read each field the marker's value names (comma-separated) row by row with <c>get</c>, which
        /// prints the text as stored — so the value survives there too, except that a CRLF line ending reads as LF.
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

        /// <summary>
        /// WinBox-native marker — the entity's WinBox labels (see <c>TikPropertyAttribute.WinboxLabel</c>) as
        /// <c>api-name=WinBox Label|…</c>. The native transport resolves a named field through its label in this
        /// router's <c>.jg</c> catalog before any name heuristic (a session field override still wins); a label the
        /// catalog does not have is ignored.
        /// <para>
        /// Added by the O/R mapper to its reads and writes of every entity with labelled properties, and only on a
        /// connection that declares <see cref="TikConnectionCapability.FieldLabels"/>. API, REST and the CLI
        /// transports silently ignore this parameter if it reaches them anyway.
        /// </para>
        /// </summary>
        public const string WinboxLabels = ".winbox-labels";
    }
}
