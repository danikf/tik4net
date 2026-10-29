using System;
using System.Linq;

namespace tik4net.Winbox
{
    /// <summary>
    /// An M2 handler: the path of numbers a request is addressed to (<c>SYS_TO</c>, <c>[20,0]</c> for
    /// <c>/interface</c>) and a <c>.jg</c> window reads (<c>path:[20,0]</c>). A value: two handlers with the same path
    /// are equal, and the text form is the bracketed path.
    /// </summary>
    /// <remarks>
    /// Built from an <c>int[]</c> only where a path comes in from outside — a parsed catalog, a caller's
    /// <see cref="WinboxNative.WinboxNativeConnection.PathOverride"/> — through the implicit conversion; everywhere else
    /// a handler is passed as this type. <c>default</c> is the empty path.
    /// </remarks>
    internal readonly struct WinboxHandler : IEquatable<WinboxHandler>
    {
        private readonly int[]? _path;

        internal WinboxHandler(params int[] path)
        {
            _path = (int[])(path ?? throw new ArgumentNullException(nameof(path))).Clone();
        }

        /// <summary>The number of elements in the path.</summary>
        internal int Count => _path?.Length ?? 0;

        /// <summary>The path element at <paramref name="index"/>.</summary>
        internal int this[int index] => (_path ?? Array.Empty<int>())[index];

        /// <summary>The path as the wire carries it (<c>SYS_TO</c>, a u32 array) — a copy.</summary>
        internal int[] ToArray() => _path == null ? Array.Empty<int>() : (int[])_path.Clone();

        public bool Equals(WinboxHandler other)
            => (_path ?? Array.Empty<int>()).SequenceEqual(other._path ?? Array.Empty<int>());

        public override bool Equals(object? obj) => obj is WinboxHandler other && Equals(other);

        public override int GetHashCode()
        {
            unchecked
            {
                int hash = 17;
                if (_path != null)
                    foreach (int n in _path) hash = hash * 31 + n;
                return hash;
            }
        }

        public static bool operator ==(WinboxHandler a, WinboxHandler b) => a.Equals(b);

        public static bool operator !=(WinboxHandler a, WinboxHandler b) => !a.Equals(b);

        /// <summary>The handler a parsed or caller-given path names.</summary>
        public static implicit operator WinboxHandler(int[] path) => new WinboxHandler(path);

        /// <summary>The bracketed path, <c>[20,0]</c> — how every message and trace names a handler.</summary>
        public override string ToString() => "[" + string.Join(",", _path ?? Array.Empty<int>()) + "]";
    }
}
