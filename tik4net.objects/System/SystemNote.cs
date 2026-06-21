using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace tik4net.Objects.System
{
    /// <summary>
    /// /system/note — login banner / MOTD (Message of the Day) settings (singleton).
    /// The note text can be displayed at router login via the console or WinBox.
    /// <para>Note: <c>=detail=</c> is rejected by this menu; plain print is used.</para>
    /// <para>See also: https://help.mikrotik.com/docs/display/ROS/System+Notes</para>
    /// </summary>
    // IncludeDetails omitted — detail= is rejected by this singleton.
    [TikEntity("/system/note", IsSingleton = true)]
    public class SystemNote
    {
        /// <summary>note — free-form text displayed as a login banner / MOTD. Can be multi-line.</summary>
        [TikProperty("note", DefaultValue = "")]
        public string Note { get; set; }

        /// <summary>show-at-login — when yes, the note is shown to users who log in via WinBox or the API. Default: yes.</summary>
        [TikProperty("show-at-login", DefaultValue = "yes")]
        public bool ShowAtLogin { get; set; }

        /// <summary>show-at-cli-login — when yes, the note is shown to users who log in via the CLI (console/SSH/Telnet). Default: no.</summary>
        [TikProperty("show-at-cli-login", DefaultValue = "no")]
        public bool ShowAtCliLogin { get; set; }

        /// <summary>Returns a human-readable summary of the login note settings.</summary>
        public override string ToString() => string.Format("note: show-at-login={0}, show-at-cli={1}, text=\"{2}\"", ShowAtLogin, ShowAtCliLogin, Note);
    }
}
