using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace tik4net.Objects.System
{
    /// <summary>
    /// /file — user-space file list on the router. Displays files stored on the router's flash/disk,
    /// including uploaded files, logs, and installed packages (.npk). Package files additionally expose
    /// architecture, build time, name, and version fields. File contents can be read and written for
    /// text files up to 60 KB; larger files require the API <c>read</c> command with offset/chunk-size.
    /// </summary>
    [TikEntity("/file", IncludeDetails = true)]
    public class File
    {
        /// <summary>
        /// .id — primary key of the row.
        /// </summary>
        [TikProperty(".id", IsReadOnly = true, IsMandatory = true)]
        public string Id { get; private set; }

        /// <summary>
        /// name — file name (including path for files in sub-directories). Writable: renaming a file
        /// is done by setting a new name value.
        /// </summary>
        [TikProperty("name", IsMandatory = true)]
        public string Name { get; set; }

        /// <summary>
        /// type — file type as reported by RouterOS (e.g. "directory", "package", ".txt file",
        /// "config file"). Read-only.
        /// </summary>
        [TikProperty("type", IsReadOnly = true)]
        public string Type { get; private set; }

        /// <summary>
        /// size — file size in bytes. Read-only.
        /// </summary>
        [TikProperty("size", IsReadOnly = true)]
        public long Size { get; private set; }

        /// <summary>
        /// creation-time — date and time the file was created. Read-only.
        /// Deprecated in RouterOS 7.16 in favour of <see cref="LastModified"/>.
        /// </summary>
        [TikProperty("creation-time", IsReadOnly = true)]
        public string/*datetime*/ CreationTime { get; private set; }

        /// <summary>
        /// last-modified — date and time of file creation or most recent modification (RouterOS 7.16+).
        /// Read-only.
        /// </summary>
        [TikProperty("last-modified", IsReadOnly = true)]
        public string/*datetime*/ LastModified { get; private set; }

        /// <summary>
        /// contents — the full text content of the file. Writable for text files up to 60 KB;
        /// reading or writing larger files requires the API <c>read</c> command.
        /// </summary>
        [TikProperty("contents")]
        public string Contents { get; set; }

        /// <summary>
        /// package-architecture — target CPU architecture of an .npk package file (e.g. "arm", "mipsbe").
        /// Only present for package files. Read-only.
        /// </summary>
        [TikProperty("package-architecture", IsReadOnly = true)]
        public string PackageArchitecture { get; private set; }

        /// <summary>
        /// package-built-time — build timestamp of an .npk package file. Only present for package files.
        /// Read-only.
        /// </summary>
        [TikProperty("package-built-time", IsReadOnly = true)]
        public string/*datetime*/ PackageBuiltTime { get; private set; }

        /// <summary>
        /// package-name — installable package name from an .npk file (e.g. "wireless", "security").
        /// Only present for package files. Read-only.
        /// </summary>
        [TikProperty("package-name", IsReadOnly = true)]
        public string PackageName { get; private set; }

        /// <summary>
        /// package-version — version string of an .npk package file (e.g. "7.14.3").
        /// Only present for package files. Read-only.
        /// </summary>
        [TikProperty("package-version", IsReadOnly = true)]
        public string PackageVersion { get; private set; }

        /// <inheritdoc/>
        public override string ToString()
        {
            return string.Format("{0} ({1}, {2} bytes)", Name, Type, Size);
        }
    }
}
