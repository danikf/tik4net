using System;
using System.ComponentModel;
using System.IO;
using System.Text;
using tik4net.Connection;

namespace tik4net
{
    /// <summary>
    /// A password kept out of a configuration file: a prefix names where the real password is, and this class reads
    /// it. A value with no prefix (or with one this class does not know, such as <c>abc:def</c>) is the password itself.
    /// </summary>
    /// <remarks>
    /// <list type="table">
    /// <listheader><term>Written as</term><description>The password is</description></listheader>
    /// <item><term><c>plain:TEXT</c></term><description><c>TEXT</c>, unchanged — the escape for a password that itself
    /// begins with a prefix (<c>plain:env:x</c> is the password <c>env:x</c>).</description></item>
    /// <item><term><c>dpapi:BASE64</c></term><description>A Windows DPAPI blob, decrypted for the account (or the
    /// machine) that encrypted it — <see cref="ProtectWithDpapi"/> makes one. Windows only.</description></item>
    /// <item><term><c>env:NAME</c></term><description>The environment variable <c>NAME</c>; set but empty is the empty
    /// password.</description></item>
    /// <item><term><c>file:PATH</c></term><description>The UTF-8 text of a file of at most 1 KiB, a relative path taken
    /// against the current directory. One trailing line break is removed (a Docker or Kubernetes secret written by
    /// <c>echo</c>); any other line break or control character is refused, so a key file or a multi-line
    /// configuration file is never sent as a password.</description></item>
    /// </list>
    /// <para>The prefix is matched case-insensitively (<c>DPAPI:</c> is <c>dpapi:</c>).</para>
    /// <para>
    /// <b>Where it applies.</b> <see cref="TikConnectionSetup.FromConnectionString(string)"/> reads every prefix,
    /// because a connection string is configuration. The constructors of <see cref="TikConnectionSetup"/> and
    /// <see cref="TikRomonAgentSetup"/>, and <see cref="ITikConnection.Open(string, string, string)"/> with its overloads,
    /// read <c>plain:</c> and <c>dpapi:</c> and refuse <c>env:</c> and <c>file:</c>: a password there is often typed by a
    /// user, and a user who could name a variable or a file, and the host the password goes to, could send its contents
    /// to their own server. Call <see cref="Resolve(string)"/> first when the value is your configuration, or wrap a
    /// user's input in <see cref="Plain"/> so that nothing in it is read as a prefix.
    /// </para>
    /// <para>
    /// What this protects against is a password readable in a file that is checked in, copied into a ticket or read by
    /// another account. It does not protect against code running as the same account, which can decrypt a DPAPI blob
    /// and read the variable or the file just as this class does. Once read, the password is an ordinary
    /// <see cref="string"/>.
    /// </para>
    /// <para>
    /// Errors name the key and the prefix, a variable's name or a file's path — never the password and never the blob.
    /// </para>
    /// </remarks>
    public static class TikPassword
    {
        private const string PlainPrefix = "plain:";
        private const string DpapiPrefix = "dpapi:";
        private const string EnvPrefix = "env:";
        private const string FilePrefix = "file:";

        /// <summary>The largest file <c>file:</c> reads, in bytes.</summary>
        internal const int MaxFileBytes = 1024;

        internal enum Prefix { None, Plain, Dpapi, Env, File }

        /// <summary>Reads a password written with any prefix (see <see cref="TikPassword"/>).</summary>
        /// <param name="password">The password, or a reference to it.</param>
        /// <returns>The password itself.</returns>
        /// <exception cref="ArgumentException">The variable is not set, the file cannot be read or is not one line of
        /// text, or the DPAPI blob does not decrypt.</exception>
        /// <exception cref="PlatformNotSupportedException"><c>dpapi:</c> off Windows.</exception>
        public static string Resolve(string password) => Resolve(password, TikPasswordSources.All);

        /// <summary>Reads a password written with a prefix that <paramref name="sources"/> allows.</summary>
        /// <param name="password">The password, or a reference to it.</param>
        /// <param name="sources">The prefixes read; <c>plain:</c> and an unprefixed value always are.</param>
        /// <returns>The password itself.</returns>
        /// <exception cref="ArgumentException">A prefix <paramref name="sources"/> does not allow, or the variable is
        /// not set, the file cannot be read or is not one line of text, or the DPAPI blob does not decrypt.</exception>
        /// <exception cref="PlatformNotSupportedException"><c>dpapi:</c> off Windows.</exception>
        public static string Resolve(string password, TikPasswordSources sources)
            => Resolve(password, sources, "password", nameof(password), credentialEntry: false);

        /// <summary>
        /// <paramref name="password"/> written so that it is taken as it is — <c>plain:</c> in front of it. For a password
        /// a user typed, passed to <see cref="ITikConnection.Open(string, string, string)"/> or a setup's constructor,
        /// so that a password that happens to begin with <c>dpapi:</c> is sent rather than decrypted.
        /// </summary>
        public static string Plain(string password)
        {
            Guard.ArgumentNotNull(password, nameof(password));
            return PlainPrefix + password;
        }

        /// <summary>
        /// Encrypts <paramref name="password"/> with Windows DPAPI and returns it as <c>dpapi:BASE64</c>, ready for a
        /// connection string's <c>password</c> key.
        /// </summary>
        /// <param name="password">The password.</param>
        /// <param name="machineScope">
        /// <c>false</c> (the default): only this Windows account on this machine can decrypt it — run this as the
        /// account the application runs as (a service account included). <c>true</c>: any process on this machine can
        /// decrypt it, so it protects only against the value leaving the machine.
        /// </param>
        /// <exception cref="PlatformNotSupportedException">Not on Windows.</exception>
        public static string ProtectWithDpapi(string password, bool machineScope = false)
        {
            Guard.ArgumentNotNull(password, nameof(password));
            if (!TikDpapi.IsSupported)
                throw new PlatformNotSupportedException("DPAPI is Windows-only; on this platform use env: or file:.");
            byte[] plain = Encoding.UTF8.GetBytes(password);
            try { return DpapiPrefix + Convert.ToBase64String(TikDpapi.Protect(plain, machineScope)); }
            finally { Array.Clear(plain, 0, plain.Length); }
        }

        /// <summary>
        /// A password already read, written so that the next reader takes it as it is: <c>plain:</c> in front only when
        /// it begins with a prefix, so that a connection that knows nothing of prefixes (a third-party
        /// <see cref="ITikConnection"/>) still receives the password itself in every other case.
        /// </summary>
        internal static string Escape(string password)
            => Split(password, out _) == Prefix.None ? password : PlainPrefix + password;

        /// <summary>
        /// A password given to a setup's constructor or to <c>Open</c>: <c>plain:</c> and <c>dpapi:</c> are read,
        /// <c>env:</c> and <c>file:</c> refused with the way out.
        /// </summary>
        internal static string ResolveCredential(string password, string paramName)
            => Resolve(password, TikPasswordSources.Dpapi, paramName, paramName, credentialEntry: true);

        /// <summary>Reads a value under <paramref name="key"/>, the name errors give it.</summary>
        internal static string Resolve(string password, TikPasswordSources sources, string key, string paramName,
            bool credentialEntry)
        {
            Guard.ArgumentNotNull(password, paramName);
            Prefix prefix = Split(password, out string rest);
            if (prefix == Prefix.None)
                return password;
            if (prefix == Prefix.Plain)
                return rest;
            if ((sources & SourceOf(prefix)) == 0)
                throw NotAllowed(prefix, sources, key, paramName, credentialEntry);
            switch (prefix)
            {
                case Prefix.Dpapi: return ReadDpapi(rest, key, paramName);
                case Prefix.Env: return ReadEnvironment(rest, key, paramName);
                default: return ReadFile(rest, key, paramName);
            }
        }

        /// <summary>The prefix <paramref name="value"/> begins with, and the rest after it.</summary>
        internal static Prefix Split(string value, out string rest)
        {
            foreach (var (text, prefix) in new[]
                     {
                         (PlainPrefix, Prefix.Plain), (DpapiPrefix, Prefix.Dpapi), (EnvPrefix, Prefix.Env), (FilePrefix, Prefix.File),
                     })
            {
                if (value.StartsWith(text, StringComparison.OrdinalIgnoreCase))
                {
                    rest = value.Substring(text.Length);
                    return prefix;
                }
            }
            rest = value;
            return Prefix.None;
        }

        internal static string PrefixText(Prefix prefix) => prefix switch
        {
            Prefix.Plain => PlainPrefix,
            Prefix.Dpapi => DpapiPrefix,
            Prefix.Env => EnvPrefix,
            Prefix.File => FilePrefix,
            _ => string.Empty,
        };

        private static TikPasswordSources SourceOf(Prefix prefix) => prefix switch
        {
            Prefix.Dpapi => TikPasswordSources.Dpapi,
            Prefix.Env => TikPasswordSources.Environment,
            _ => TikPasswordSources.File,
        };

        private static ArgumentException NotAllowed(Prefix prefix, TikPasswordSources sources, string key, string paramName,
            bool credentialEntry)
        {
            string text = PrefixText(prefix);
            string escape = $" To send a password that begins with '{text}' as it is, write it as '{PlainPrefix}{text}…' "
                            + "(TikPassword.Plain).";
            return new ArgumentException(credentialEntry
                ? $"{key}: '{text}' is read only from a connection string or by TikPassword.Resolve, so that a password a "
                  + "user types cannot read a variable or a file." + escape
                : $"{key}: '{text}' is not read here (allowed: {sources})." + escape, paramName);
        }

        private static string ReadDpapi(string base64, string key, string paramName)
        {
            if (!TikDpapi.IsSupported)
                throw new PlatformNotSupportedException($"{key}: dpapi: is Windows-only; on this platform use env: or file: "
                                                        + "in a connection string, or TikPassword.Resolve.");
            byte[] blob;
            try { blob = Convert.FromBase64String(base64.Trim()); }
            catch (FormatException ex)
            {
                throw new ArgumentException($"{key}: the dpapi: value is not base64.", paramName, ex);
            }
            byte[] plain;
            try { plain = TikDpapi.Unprotect(blob); }
            catch (Win32Exception ex)
            {
                throw new ArgumentException($"{key}: the dpapi: value does not decrypt for this user on this machine; it "
                                            + "decrypts only for the account (or the machine) that encrypted it.", paramName, ex);
            }
            try { return StrictUtf8.GetString(plain); }
            catch (DecoderFallbackException ex)
            {
                throw new ArgumentException($"{key}: the dpapi: value does not decrypt to UTF-8 text.", paramName, ex);
            }
            finally { Array.Clear(plain, 0, plain.Length); }
        }

        private static string ReadEnvironment(string name, string key, string paramName)
        {
            if (name.Length == 0)
                throw new ArgumentException($"{key}: env: names no variable.", paramName);
            return Environment.GetEnvironmentVariable(name)
                   ?? throw new ArgumentException($"{key}: the environment variable '{name}' is not set.", paramName);
        }

        private static string ReadFile(string path, string key, string paramName)
        {
            if (path.Length == 0)
                throw new ArgumentException($"{key}: file: names no file.", paramName);
            byte[] bytes = ReadAtMost(path, MaxFileBytes + 1, key, paramName);
            if (bytes.Length > MaxFileBytes)
                throw new ArgumentException($"{key}: the file '{path}' is larger than {MaxFileBytes} bytes; file: reads a "
                                            + "one-line password.", paramName);
            string text;
            try { text = StrictUtf8.GetString(bytes); }
            catch (DecoderFallbackException ex)
            {
                throw new ArgumentException($"{key}: the file '{path}' is not UTF-8 text.", paramName, ex);
            }
            finally { Array.Clear(bytes, 0, bytes.Length); }

            if (text.Length > 0 && text[0] == '﻿')
                text = text.Substring(1);
            if (text.EndsWith("\r\n", StringComparison.Ordinal))
                text = text.Substring(0, text.Length - 2);
            else if (text.EndsWith("\n", StringComparison.Ordinal))
                text = text.Substring(0, text.Length - 1);
            foreach (char c in text)
            {
                if (c < 0x20 || c == 0x7F)
                    throw new ArgumentException($"{key}: the file '{path}' holds a line break or another control character; "
                                                + "file: reads a one-line password (one trailing line break is allowed).", paramName);
            }
            return text;
        }

        private static byte[] ReadAtMost(string path, int limit, string key, string paramName)
        {
            try
            {
                using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                var buffer = new byte[limit];
                int total = 0, read;
                while (total < limit && (read = stream.Read(buffer, total, limit - total)) > 0)
                    total += read;
                if (total == limit)
                    return buffer;
                var result = new byte[total];
                Array.Copy(buffer, result, total);
                Array.Clear(buffer, 0, buffer.Length);
                return result;
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is NotSupportedException
                                       || ex is System.Security.SecurityException || ex is ArgumentException)
            {
                throw new ArgumentException($"{key}: the file '{path}' cannot be read ({ex.GetType().Name}).", paramName, ex);
            }
        }

        private static readonly Encoding StrictUtf8 = new UTF8Encoding(false, throwOnInvalidBytes: true);
    }
}
