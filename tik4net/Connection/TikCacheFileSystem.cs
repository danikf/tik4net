using System;
using System.IO;
using System.Text;
using System.Threading;

namespace tik4net.Connection
{
    /// <summary>
    /// The file operations of the on-disk caches under <c>CatalogCachePath</c> (the WinBox <c>.jg</c> plugins, the CLI
    /// menu grammar), behind one seam so that a test can make each of them fail.
    /// </summary>
    /// <remarks>
    /// Every member may throw; the callers are the ones that turn a failure into "no cache" — a cache problem must never
    /// reach the caller of a connection (see <c>CliSchemaStore</c>).
    /// </remarks>
    internal interface ITikCacheFileSystem
    {
        /// <summary>The file's text, or <c>null</c> when there is no such file.</summary>
        string? ReadAllText(string path);

        /// <summary>
        /// Writes <paramref name="text"/> so that a reader sees the old file or the new one, never a half-written one;
        /// creates the directory.
        /// </summary>
        void WriteAtomically(string path, string text);

        /// <summary>
        /// Takes the cross-process lock beside <paramref name="path"/> (<c>&lt;path&gt;.lock</c>), waiting at most
        /// <paramref name="timeout"/>; <c>null</c> when it could not be had. Disposing releases it.
        /// </summary>
        IDisposable? TryLock(string path, TimeSpan timeout);
    }

    /// <summary>The real file system.</summary>
    internal sealed class TikCacheFileSystem : ITikCacheFileSystem
    {
        internal static readonly TikCacheFileSystem Default = new TikCacheFileSystem();

        public string? ReadAllText(string path)
            => File.Exists(path) ? File.ReadAllText(path, Encoding.UTF8) : null;

        public void WriteAtomically(string path, string text)
        {
            string? dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir))
                Directory.CreateDirectory(dir!);
            WriteAtomicallyCore(path, text);
        }

        /// <summary>
        /// Writes a temp file beside <paramref name="path"/> and moves it into place, so a reader never sees a partial
        /// file. When another process moved its copy into place first, theirs is kept: a <c>.jg</c> plugin is
        /// content-addressed (theirs is this file), and the CLI grammar store writes under its lock and merges first.
        /// </summary>
        internal static void WriteAtomicallyCore(string path, string text)
        {
            string temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                File.WriteAllText(temp, text, new UTF8Encoding(false));
                try
                {
                    if (File.Exists(path))
                        File.Replace(temp, path, null);
                    else
                        File.Move(temp, path);
                }
                catch (IOException) when (File.Exists(path))
                {
                    // Another process moved its copy into place first (see the summary).
                }
            }
            finally
            {
                try { if (File.Exists(temp)) File.Delete(temp); }
                catch { /* a leftover .tmp is harmless: nothing reads it */ }
            }
        }

        public IDisposable? TryLock(string path, TimeSpan timeout)
        {
            string lockPath = path + ".lock";
            string? dir = Path.GetDirectoryName(lockPath);
            if (!string.IsNullOrEmpty(dir))
                Directory.CreateDirectory(dir!);
            var deadline = DateTime.UtcNow + timeout;
            while (true)
            {
                try
                {
                    // FileShare.None: one holder at a time, across processes; the OS releases it if the holder dies.
                    return new FileStream(lockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None, 1,
                        FileOptions.DeleteOnClose);
                }
                catch (IOException) when (DateTime.UtcNow < deadline)
                {
                    Thread.Sleep(50);
                }
                catch (IOException)
                {
                    return null;
                }
                catch (UnauthorizedAccessException)
                {
                    return null;
                }
            }
        }
    }
}
