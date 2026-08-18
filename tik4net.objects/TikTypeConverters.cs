using System;
using System.Collections.Generic;
using System.Linq;

namespace tik4net.Objects
{
    /// <summary>
    /// Registry of <see cref="ITikTypeConverter"/>s — the mapper's extension point for property types it
    /// does not handle natively.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Register during application start-up. Registration order matters only between converters that claim
    /// the same type, where the first registered wins; the mapper's built-in types are always tried before
    /// any converter (see <see cref="ITikTypeConverter"/> for why).
    /// </para>
    /// <para>
    /// Registering after entities have already been used is supported: an accessor whose type nothing
    /// claimed resolves again on its next conversion, so a late registration takes effect rather than being
    /// silently ignored. It is still worth registering early — the resolution is a linear scan.
    /// </para>
    /// </remarks>
    /// <seealso cref="ITikTypeConverter"/>
    public static class TikTypeConverters
    {
        private static readonly object _lockObj = new object();
        private static ITikTypeConverter[] _converters = new ITikTypeConverter[0];

        /// <summary>
        /// The registered converters, in registration order.
        /// </summary>
        public static IEnumerable<ITikTypeConverter> Registered
        {
            get { return _converters; }
        }

        /// <summary>
        /// Registers a converter for property types the mapper does not handle natively.
        /// </summary>
        /// <param name="converter">The converter. Must be thread-safe and stateless.</param>
        /// <exception cref="ArgumentNullException"><paramref name="converter"/> is null.</exception>
        public static void Register(ITikTypeConverter converter)
        {
            if (converter == null)
                throw new ArgumentNullException("converter");

            lock (_lockObj)
            {
                // Copy-on-write: Resolve runs on the mapper's hot path and must never take the lock.
                var updated = new ITikTypeConverter[_converters.Length + 1];
                Array.Copy(_converters, updated, _converters.Length);
                updated[_converters.Length] = converter;
                _converters = updated;
            }
        }

        /// <summary>
        /// Removes a previously registered converter.
        /// </summary>
        /// <param name="converter">The instance passed to <see cref="Register(ITikTypeConverter)"/>.</param>
        /// <returns>True when it was registered and has been removed.</returns>
        /// <remarks>
        /// An accessor that already resolved this converter keeps using it — the entity metadata is cached
        /// for the life of the process. Unregistering is meant for tests, not for swapping behaviour at runtime.
        /// </remarks>
        public static bool Unregister(ITikTypeConverter converter)
        {
            if (converter == null)
                return false;

            lock (_lockObj)
            {
                if (!_converters.Contains(converter))
                    return false;

                _converters = _converters.Where(c => !ReferenceEquals(c, converter)).ToArray();
                return true;
            }
        }

        /// <summary>
        /// Finds the converter for <paramref name="type"/>, or null when none claims it.
        /// </summary>
        /// <param name="type">The property type, already unwrapped from <see cref="Nullable{T}"/>.</param>
        /// <returns>The first registered converter whose <see cref="ITikTypeConverter.CanConvert"/> returns true.</returns>
        public static ITikTypeConverter? Resolve(Type type)
        {
            var converters = _converters;   //one read: Register may swap the array under us
            for (int i = 0; i < converters.Length; i++)
            {
                if (converters[i].CanConvert(type))
                    return converters[i];
            }
            return null;
        }
    }
}
