using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace tik4net.Objects.System
{
    /// <summary>
    /// /system/package — installed RouterOS software packages (read-only list).
    /// Each entry describes one installed package: its name, version, size, and
    /// whether it is enabled. Package state (enable/disable) is changed via the
    /// <see cref="SystemPackageConnectionExtensions.Enable"/> /
    /// <see cref="SystemPackageConnectionExtensions.Disable"/> helpers rather than
    /// through the regular <c>set</c> command (which accepts no parameters).
    /// <para>See also: https://help.mikrotik.com/docs/display/ROS/Packages</para>
    /// </summary>
    [TikEntity("/system/package", IncludeDetails = true, IsReadOnly = true)]
    public class SystemPackage
    {
        /// <summary>.id — primary key of the row.</summary>
        [TikProperty(".id", IsReadOnly = true, IsMandatory = true)]
        public string Id { get; private set; }

        /// <summary>name — package name (e.g. "routeros", "wireless", "user-manager").</summary>
        [TikProperty("name", IsReadOnly = true, IsMandatory = true)]
        public string Name { get; private set; }

        /// <summary>version — installed package version string (e.g. "7.21.4").</summary>
        [TikProperty("version", IsReadOnly = true)]
        public string Version { get; private set; }

        /// <summary>build-time — date and time when this package was built. Read-only.</summary>
        [TikProperty("build-time", IsReadOnly = true)]
        public string/*datetime*/ BuildTime { get; private set; }

        /// <summary>scheduled — action scheduled for this package at next reboot (e.g. "scheduled for uninstall"). Empty when nothing is scheduled. Read-only.</summary>
        [TikProperty("scheduled", IsReadOnly = true)]
        public string Scheduled { get; private set; }

        /// <summary>size — installed package size in bytes. Read-only.</summary>
        [TikProperty("size", IsReadOnly = true)]
        public string Size { get; private set; }

        /// <summary>available — when true a newer version is available for download. Read-only.</summary>
        [TikProperty("available", IsReadOnly = true)]
        public bool Available { get; private set; }

        /// <summary>disabled — when true the package is scheduled to be disabled at next reboot. Changed via Enable/Disable commands, not via set. Read-only.</summary>
        [TikProperty("disabled", IsReadOnly = true)]
        public bool Disabled { get; private set; }

        /// <summary>Returns a human-readable summary of the package.</summary>
        public override string ToString() => string.Format("{0} {1}{2}", Name, Version, Disabled ? " [disabled]" : string.Empty);
    }

    /// <summary>Connection extension methods for <see cref="SystemPackage"/>.</summary>
    public static class SystemPackageConnectionExtensions
    {
        /// <summary>
        /// Enables the specified package (takes effect after reboot).
        /// Equivalent to <c>/system/package enable .id=&lt;id&gt;</c>.
        /// </summary>
        public static void Enable(this ITikConnection connection, SystemPackage package)
        {
            var cmd = connection.CreateCommand("/system/package/enable",
                connection.CreateParameter(".id", package.Id, TikCommandParameterFormat.Filter));
            cmd.ExecuteNonQuery();
        }

        /// <summary>
        /// Disables the specified package (takes effect after reboot).
        /// Equivalent to <c>/system/package disable .id=&lt;id&gt;</c>.
        /// </summary>
        public static void Disable(this ITikConnection connection, SystemPackage package)
        {
            var cmd = connection.CreateCommand("/system/package/disable",
                connection.CreateParameter(".id", package.Id, TikCommandParameterFormat.Filter));
            cmd.ExecuteNonQuery();
        }
    }
}
