using System;

namespace tik4net
{
    /// <summary>
    /// Where a password written with a prefix may be read from — see <see cref="TikPassword"/>. <c>plain:</c> and a
    /// value with no prefix are always taken as they are.
    /// </summary>
    [Flags]
    public enum TikPasswordSources
    {
        /// <summary>No source: only <c>plain:</c> and unprefixed values. <c>dpapi:</c>, <c>env:</c> and <c>file:</c> throw.</summary>
        None = 0,

        /// <summary><c>dpapi:BASE64</c> — a Windows DPAPI blob, decrypted for the current user or machine.</summary>
        Dpapi = 1,

        /// <summary><c>env:NAME</c> — an environment variable.</summary>
        Environment = 2,

        /// <summary><c>file:PATH</c> — a one-line text file.</summary>
        File = 4,

        /// <summary>Every source.</summary>
        All = Dpapi | Environment | File,
    }
}
