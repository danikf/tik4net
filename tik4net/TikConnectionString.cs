using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Globalization;
using System.Linq;
using System.Text;

namespace tik4net
{
    /// <summary>
    /// The connection-string form of a <see cref="TikConnectionSetup"/> — see
    /// <see cref="TikConnectionSetup.FromConnectionString"/> for the keys.
    /// </summary>
    /// <remarks>
    /// ADO.NET's syntax, parsed by <see cref="DbConnectionStringBuilder"/>: <c>key=value</c> pairs separated by <c>;</c>,
    /// keys case-insensitive, a value holding <c>;</c> or <c>=</c> quoted with <c>"</c> or <c>'</c>. Every key is a property
    /// of the setup; an unknown key is refused rather than ignored, so a typo cannot quietly open with a default.
    /// </remarks>
    internal static class TikConnectionString
    {
        // Canonical key -> its aliases. The canonical spelling is what ToString writes.
        private static readonly (string Key, string[] Aliases)[] Keys =
        {
            ("transport", new[] { "connectionType" }),
            ("host", new[] { "server", "address" }),
            ("routerMac", new[] { "mac" }),
            ("user", new[] { "username", "uid" }),
            ("password", new[] { "pwd" }),
            ("port", new string[0]),
            ("connectTimeout", new string[0]),
            ("receiveTimeout", new string[0]),
            ("sendTimeout", new string[0]),
            ("encoding", new string[0]),
            ("allowInvalidCertificate", new string[0]),
            ("cancellationMode", new string[0]),
            ("cliReadPageSize", new string[0]),
            ("cliFieldSeparator", new string[0]),
            ("validateWrites", new string[0]),
            ("sendTagWithSyncCommand", new string[0]),
            ("debug", new[] { "debugEnabled" }),
            ("romon.host", new string[0]),
            ("romon.user", new string[0]),
            ("romon.password", new string[0]),
            ("romon.port", new string[0]),
        };

        internal static IEnumerable<string> CanonicalKeys => Keys.Select(k => k.Key);

        private static string Canonical(string key)
        {
            foreach (var (canonical, aliases) in Keys)
                if (string.Equals(key, canonical, StringComparison.OrdinalIgnoreCase)
                    || aliases.Any(a => string.Equals(key, a, StringComparison.OrdinalIgnoreCase)))
                    return canonical;
            throw new ArgumentException(string.Format(CultureInfo.InvariantCulture,
                "Unknown key '{0}' in the connection string. Known keys: {1}.", key, string.Join(", ", CanonicalKeys)),
                "connectionString");
        }

        internal static TikConnectionSetup Parse(string connectionString)
        {
            Guard.ArgumentNotNullOrEmptyString(connectionString, nameof(connectionString));
            var builder = new DbConnectionStringBuilder { ConnectionString = connectionString };
            var values = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (string key in builder.Keys)
            {
                string canonical = Canonical(key);
                if (values.ContainsKey(canonical))
                    throw new ArgumentException($"The connection string names '{canonical}' twice.", nameof(connectionString));
                values[canonical] = Convert.ToString(builder[key], CultureInfo.InvariantCulture) ?? string.Empty;
            }

            string? Take(string key) => values.TryGetValue(key, out var v) ? v : null;

            string? host = Take("host");
            string? mac = Take("routerMac");
            string user = Take("user") ?? throw new ArgumentException("The connection string has no 'user'.", nameof(connectionString));
            string? romonHost = Take("romon.host");

            TikRouterAddress address;
            if (romonHost != null)
                address = TikRouterAddress.FromRomonId(host ?? throw new ArgumentException(
                    "A RoMON connection string names the target's RoMON id as 'host'.", nameof(connectionString)));
            else if (host != null && mac != null)
                address = TikRouterAddress.FromHostAndMac(host, mac);
            else if (host != null)
                address = TikRouterAddress.FromHost(host);
            else if (mac != null)
                address = TikRouterAddress.FromMac(mac);
            else
                throw new ArgumentException("The connection string names neither 'host' nor 'routerMac'.", nameof(connectionString));

            var setup = new TikConnectionSetup(address, user, Take("password") ?? string.Empty);
            foreach (var kv in values)
            {
                string v = kv.Value;
                switch (kv.Key)
                {
                    case "transport": setup.ConnectionType = ParseEnum<TikConnectionType>(kv.Key, v); break;
                    case "port": setup.Port = ParseInt(kv.Key, v); break;
                    case "connectTimeout": setup.ConnectTimeout = ParseTime(kv.Key, v); break;
                    case "receiveTimeout": setup.ReceiveTimeout = ParseTime(kv.Key, v); break;
                    case "sendTimeout": setup.SendTimeout = ParseTime(kv.Key, v); break;
                    case "encoding": setup.Encoding = ParseEncoding(v); break;
                    case "allowInvalidCertificate": setup.AllowInvalidCertificate = ParseBool(kv.Key, v); break;
                    case "cancellationMode": setup.CancellationMode = ParseEnum<TikCancellationMode>(kv.Key, v); break;
                    case "cliReadPageSize": setup.CliReadPageSize = ParseInt(kv.Key, v); break;
                    case "cliFieldSeparator": setup.CliFieldSeparator = v.Length == 0 ? null : v; break;
                    case "validateWrites": setup.ValidateWrites = ParseBool(kv.Key, v); break;
                    case "sendTagWithSyncCommand": setup.SendTagWithSyncCommand = ParseBool(kv.Key, v); break;
                    case "debug": setup.DebugEnabled = ParseBool(kv.Key, v); break;
                    // host, routerMac with a host, user, password and the RoMON keys are read above. A MAC given
                    // with a host also goes into the address, as the RouterMac option does.
                }
            }

            if (romonHost != null)
            {
                setup.RomonAgentSetup = new TikRomonAgentSetup(romonHost,
                    Take("romon.user") ?? throw new ArgumentException("A RoMON connection string needs 'romon.user'.", nameof(connectionString)),
                    Take("romon.password") ?? string.Empty);
                if (Take("romon.port") is string romonPort)
                    setup.RomonAgentSetup.Port = ParseInt("romon.port", romonPort);
            }
            else if (Take("romon.user") != null || Take("romon.password") != null || Take("romon.port") != null)
                throw new ArgumentException("The RoMON keys need 'romon.host', the agent.", nameof(connectionString));

            return setup;
        }

        // The setup as a connection string, the password masked: a setup written to a log must not carry it.
        internal static string Format(TikConnectionSetup setup)
        {
            var parts = new List<string>();
            void Add(string key, object? value)
            {
                if (value == null) return;
                string text = value is bool b ? (b ? "true" : "false")
                    : value is TimeSpan t ? t.ToString("c", CultureInfo.InvariantCulture)
                    : Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty;
                parts.Add(key + "=" + (text.IndexOfAny(new[] { ';', '=', '"', '\'' }) >= 0 || text.Trim() != text
                    ? "\"" + text.Replace("\"", "\"\"") + "\"" : text));
            }

            Add("transport", setup.ConnectionType);
            Add("host", setup.Address.RomonId ?? setup.Host);
            Add("routerMac", setup.RouterMac ?? setup.Address.Mac);
            Add("user", setup.User);
            parts.Add("password=***");
            Add("port", setup.Port);
            if (setup.RomonAgentSetup is TikRomonAgentSetup romon)
            {
                Add("romon.host", romon.Address.Host ?? romon.Address.Mac);
                Add("romon.user", romon.User);
                parts.Add("romon.password=***");
                Add("romon.port", romon.Port);
            }
            return string.Join(";", parts);
        }

        private static int ParseInt(string key, string v)
            => int.TryParse(v, NumberStyles.Integer, CultureInfo.InvariantCulture, out int n) ? n
               : throw new ArgumentException($"'{key}={v}' is not a whole number.", "connectionString");

        private static bool ParseBool(string key, string v)
            => string.Equals(v, "true", StringComparison.OrdinalIgnoreCase) || string.Equals(v, "yes", StringComparison.OrdinalIgnoreCase) ? true
             : string.Equals(v, "false", StringComparison.OrdinalIgnoreCase) || string.Equals(v, "no", StringComparison.OrdinalIgnoreCase) ? false
             : throw new ArgumentException($"'{key}={v}' is not true or false.", "connectionString");

        // Seconds, ADO.NET's way ("15", "2.5"), or a TimeSpan ("00:00:15").
        private static TimeSpan ParseTime(string key, string v)
            => double.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out double seconds) && seconds >= 0
                ? TimeSpan.FromSeconds(seconds)
             : TimeSpan.TryParse(v, CultureInfo.InvariantCulture, out TimeSpan span) && span >= TimeSpan.Zero ? span
             : throw new ArgumentException($"'{key}={v}' is neither seconds nor a TimeSpan.", "connectionString");

        private static TEnum ParseEnum<TEnum>(string key, string v) where TEnum : struct
            => Enum.TryParse(v, ignoreCase: true, out TEnum value) && Enum.IsDefined(typeof(TEnum), value) && !int.TryParse(v, out _)
                ? value
                : throw new ArgumentException($"'{key}={v}' is not one of {string.Join(", ", Enum.GetNames(typeof(TEnum)))}.", "connectionString");

        private static Encoding ParseEncoding(string v)
        {
            try { return Encoding.GetEncoding(v); }
            catch (ArgumentException ex)
            {
                throw new ArgumentException($"'encoding={v}' is not an encoding name (utf-8, us-ascii, …).", "connectionString", ex);
            }
        }
    }
}
