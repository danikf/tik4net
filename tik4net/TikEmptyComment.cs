using System;

namespace tik4net
{
    /// <summary>
    /// RouterOS has no empty comment: <c>set comment=""</c> clears it, and the binary API and REST then leave the word
    /// out of the row, as for a row that never had one. The RouterOS 6 CLI (<c>comment=;</c>, 6.49.13) and WinBox native
    /// (an empty string on the comment key, 6.49.13 and 7.24.4) deliver it anyway; those transports drop it, so a row
    /// without a comment reads the same on every transport.
    /// </summary>
    internal static class TikEmptyComment
    {
        internal const string Field = "comment";

        /// <summary>True for an empty <c>comment</c> — a word the API would not have printed.</summary>
        internal static bool IsNoComment(string? field, string? value)
            => string.IsNullOrEmpty(value) && string.Equals(field, Field, StringComparison.OrdinalIgnoreCase);
    }
}
