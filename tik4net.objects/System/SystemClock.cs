using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace tik4net.Objects.System
{
    /// <summary>
    /// /system/clock — system date, time and timezone configuration.
    /// Allows reading and setting the current time, date and timezone.
    /// This is a singleton entity (no .id); use <see cref="TikConnectionExtensions.LoadSingle{T}"/> to load it.
    /// </summary>
    [TikEntity("/system/clock", IsSingleton = true)]
    public class SystemClock
    {
        /// <summary>
        /// time — current system time in HH:MM:SS format.
        /// This field is settable via /system/clock set time=...
        /// </summary>
        [TikProperty("time", ChangesOnItsOwn = true, WinboxLabel = "Time")]
        public TikField<string?> Time { get; set; }

        /// <summary>
        /// date — current system date (time of day zero), the router's local date. Read from either spelling
        /// (7.x <c>2026-06-18</c>, 6.x <c>jun/18/2026</c>), written as <c>jun/18/2026</c>, which every version accepts
        /// (see <see cref="TikDateTimeHelper"/>).
        /// </summary>
        [TikProperty("date", ChangesOnItsOwn = true, WinboxLabel = "Date")]
        public TikField<DateTime?> Date { get; set; }

        /// <summary>
        /// time-zone-name — timezone name (IANA identifier, e.g. "Europe/Prague") or "manual"
        /// to use a manually configured GMT offset. Default: manual.
        /// WinBox: "Time Zone Name"
        /// </summary>
        [TikProperty("time-zone-name", DefaultValue = "manual", WinboxLabel = "Time Zone Name")]
        public TikField<string?> TimeZoneName { get; set; }

        /// <summary>
        /// time-zone-autodetect — when yes, the timezone is automatically detected via the public IP address.
        /// Default: yes.
        /// WinBox: "Time Zone Autodetect"
        /// </summary>
        [TikProperty("time-zone-autodetect", DefaultValue = "yes", WinboxLabel = "Time Zone Autodetect")]
        public TikField<bool?> TimeZoneAutodetect { get; set; }

        /// <summary>
        /// gmt-offset — current value of the GMT offset used by the system, after applying the base
        /// timezone offset and any active daylight saving time offset. Format: [+|-]HH:MM. Read-only.
        /// WinBox: "GMT Offset"
        /// </summary>
        [TikProperty("gmt-offset", IsReadOnly = true, WinboxLabel = "GMT Offset")]
        public TikField<string?> GmtOffset { get; private set; }

        /// <summary>
        /// dst-active — has the value yes (true) while daylight saving time of the current timezone is active.
        /// Read-only.
        /// WinBox: "DST Active"
        /// </summary>
        [TikProperty("dst-active", IsReadOnly = true, WinboxLabel = "DST Active")]
        public TikField<bool?> DstActive { get; private set; }

        /// <summary>Returns a human-readable summary of the current clock state.</summary>
        public override string ToString()
        {
            return string.Format("{0} {1} ({2})", Date, Time, TimeZoneName);
        }
    }
}
