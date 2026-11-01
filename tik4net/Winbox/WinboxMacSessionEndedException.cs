namespace tik4net.Winbox
{
    /// <summary>
    /// The router ended a WinBox-over-MAC session (<c>PKT_END</c>). Reported as TCP reports the router's FIN, an
    /// <see cref="System.IO.IOException"/>: the stream is broken, and whether a command in flight ran is not known.
    /// Its own type so that the terminal open, where no command has run, can tell it apart from a broken socket.
    /// </summary>
    internal sealed class WinboxMacSessionEndedException : System.IO.IOException
    {
        internal WinboxMacSessionEndedException()
            : base("WinBox over MAC: the router ended this session (PKT_END). Open a new connection to continue.")
        {
        }
    }
}
