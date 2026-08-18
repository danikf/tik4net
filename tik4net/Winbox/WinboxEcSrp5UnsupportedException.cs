using System;

namespace tik4net.Winbox
{
    /// <summary>
    /// Marks the one condition that justifies falling back from EC-SRP5 to the pre-6.43 MD5 handshake:
    /// the router gave no sign of understanding the EC-SRP5 opening frame.
    /// </summary>
    /// <remarks>
    /// It exists so the fallback is not selected by a substring test. Deciding it with
    /// <c>catch (Exception ex) when (ex.Message.IndexOf("EC-SRP5") &gt;= 0)</c> takes control flow from the
    /// wording of a message, and so silently reclassifies any future message that happens to name the
    /// algorithm. <see cref="Reason"/> carries the evidence, because the fallback path
    /// discards it otherwise: when legacy auth then fails too, the user sees "wrong username or
    /// password" for what was really a lost EC-SRP5 exchange.
    /// </remarks>
    internal sealed class WinboxEcSrp5UnsupportedException : Exception
    {
        /// <summary>What the router did (or failed to do) that led us to conclude EC-SRP5 is unavailable.</summary>
        internal string Reason { get; }

        internal WinboxEcSrp5UnsupportedException(string reason, Exception? innerException = null)
            : base("EC-SRP5 not supported by server: " + reason, innerException)
        {
            Reason = reason;
        }
    }
}
