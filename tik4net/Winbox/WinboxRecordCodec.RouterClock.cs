using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using tik4net.Diagnostics;

namespace tik4net.Winbox
{
    internal sealed partial class WinboxRecordCodec
    {
        /// <summary>
        /// The router's uptime in seconds, read once per connection and advanced by the local clock — which
        /// is exactly what webfig does (<c>sysres.uptimediff = uptime - getNow()</c>, then
        /// <c>getUptime() = getNow() + uptimediff</c>). Returns <c>null</c> when it cannot be read, so an
        /// <c>age</c> falls back to its raw text rather than to a number computed from a guessed origin.
        /// </summary>
        /// <remarks>
        /// The handler and key are webfig's own: <c>fetchBoardInfo</c> posts a get-singleton to <c>[24,2]</c>
        /// and takes <c>u1</c>. Caching it is not an optimisation but the same design decision webfig made —
        /// an age field appears on many rows of one table, and a round trip per row would make a certificate
        /// list N+1 requests. The drift over a session is the drift of the local clock against the router's,
        /// which for a value printed to the second is irrelevant, and a reconnect re-reads it.
        /// </remarks>
        private long? RouterUptimeSeconds()
        {
            lock (_uptimeLock)
            {
                if (_uptimeAtFetch.HasValue)
                    return _uptimeAtFetch.Value + (long)_sinceUptimeFetch.Elapsed.TotalSeconds;
                if (_uptimeUnreadable) return null;
            }

            long fetched;
            try
            {
                // Both ends of the round trip, so the reply can be placed at its midpoint: the router
                // sampled its counters somewhere inside this interval, and a relative dateandtime is only
                // as good as the instant those counters are pinned to.
                DateTime before = DateTime.UtcNow;
                var board = _ops.GetSingleton(BoardInfoHandler);
                DateTime after = DateTime.UtcNow;
                if (board == null || !board.TryGetValue(BoardUptimeKey, out var up)
                    || !WinboxValueFormat.TryToInt64(up.Value, out fetched))
                {
                    lock (_uptimeLock) _uptimeUnreadable = true;
                    TraceUptimeUnreadable(null);
                    return null;
                }
                // The same window's jiffies (`{name:'jiffies',type:'number',id:'u2e'}`, nonpublic) is the
                // SAME counter as u1 at a hundred times the resolution - measured on 7.24, 1137 units in
                // 11.367 s, and u1 = jiffies/100 to the unit. It is the clock a `relative` dateandtime is
                // expressed on, so an origin taken from u1's whole seconds throws away exactly the fraction
                // that decides which second the answer falls in.
                long? jiffies = null;
                if (board.TryGetValue(BoardJiffiesKey, out var jfy)
                    && WinboxValueFormat.TryToInt64(jfy.Value, out long jval)) jiffies = jval;
                lock (_uptimeLock)
                {
                    _jiffiesAtFetch = jiffies;
                    _boardFetchedAt = before.AddTicks((after - before).Ticks / 2);
                }
            }
            catch (Exception ex)
            {
                // Unlike a reference table this IS memoized as unreadable: every age field on the connection
                // would otherwise retry the same singleton, and a handler that has no board info will not
                // grow one mid-session.
                lock (_uptimeLock) _uptimeUnreadable = true;
                TraceUptimeUnreadable(ex);
                return null;
            }

            lock (_uptimeLock)
            {
                _uptimeAtFetch = fetched;
                _sinceUptimeFetch = Stopwatch.StartNew();
                return fetched;
            }
        }

        /// <summary>
        /// The moment the router booted, in HUNDREDTHS of a unix-epoch second in the ROUTER's own timezone -
        /// the origin a <c>relative</c> <c>dateandtime</c> counts from. <c>null</c> when a half is
        /// unreadable, so such a field keeps its raw value rather than being dated from a guessed origin.
        /// </summary>
        /// <remarks>
        /// <para>webfig computes the same thing inline and one resolution coarser:
        /// <c>getNow()-getUptime()+sysres.GMToffset</c>, where its uptime is the board singleton's whole
        /// SECONDS. Whole seconds are not enough here - the origin then lands anywhere inside the second the
        /// counter last ticked in, which is a full second of error on a value printed to the second. The same
        /// singleton carries <c>jiffies</c> (<c>u2e</c>), the identical counter in hundredths, and that is
        /// what this uses; <c>u1</c> is the fallback for a board that does not send it.</para>
        /// <para>Memoized for the connection, anchored to the instant the board reply was taken (see
        /// <see cref="RouterUptimeSeconds"/>): boot does not move while the session lives. It is the SAME
        /// frame the absolute <c>dateandtime</c> values ride in - a certificate's epoch value is the API's
        /// timestamp with no timezone shift applied - which is why the offset is added, not subtracted.</para>
        /// <para><b>The router's own answer for these fields is not stable to the second</b>, so neither is
        /// this one. RouterOS re-derives the wall-clock time from the same uptime counter on every print, and
        /// two API reads minutes apart answered <c>21:43:13</c> and <c>21:43:12</c> for one unchanged link
        /// (7.24). What the origin's precision buys is that OUR answer is no further out than the router's
        /// own wobble - not an agreement that the router itself does not offer.</para>
        /// </remarks>
        private long? RouterBootEpochHundredths()
        {
            lock (_uptimeLock)
            {
                if (_bootEpochHundredths.HasValue) return _bootEpochHundredths;
            }

            // Populates the jiffies counter and the fetch instant as a side effect of the same singleton.
            long? uptime = RouterUptimeSeconds();
            if (uptime == null) return null;
            long? routerNow = RouterClockEpochSeconds();
            if (routerNow == null) return null;

            lock (_uptimeLock)
            {
                long sinceBoot = _jiffiesAtFetch ?? (uptime.Value * 100);
                // The two singletons are two round trips apart, and the counter has moved in between. The
                // local clock is trusted for that ELAPSED interval only - a few tens of milliseconds, where
                // a skewed clock is still a good stopwatch - never for the absolute time.
                long betweenReads = (long)Math.Round((_clockFetchedAt - _boardFetchedAt).TotalSeconds * 100);
                // The router prints its clock to the second, so the true instant is somewhere inside that
                // second; its midpoint is the estimate that is not biased one way. Half a second is what is
                // left of the error, against a router whose own answer for these fields moves by a whole one
                // between prints.
                _bootEpochHundredths = (routerNow.Value * 100) + 50 - sinceBoot - betweenReads;
                return _bootEpochHundredths;
            }
        }

        /// <summary>
        /// The router's own current time, as epoch seconds in its own timezone, read once per connection from
        /// the clock singleton <c>[24,0]</c>. <c>null</c> when it cannot be read.
        /// </summary>
        /// <remarks>
        /// Key <c>0x7</c> is the whole clock, not an offset: 1787696548 read back while the router's own
        /// <c>/system/clock</c> said <c>2026-08-25 22:22:28</c> - the local time, expressed as an epoch. Its
        /// siblings on the same record are the parts webfig uses (<c>0x1b</c> the GMT offset, <c>0x1c</c> the
        /// DST offset, <c>0x1e</c> the time of day), and none of them is needed once the whole value is here.
        /// </remarks>
        private long? RouterClockEpochSeconds()
        {
            lock (_uptimeLock)
            {
                if (_routerNowSeconds.HasValue) return _routerNowSeconds;
                if (_routerClockUnreadable) return null;
            }

            long fetched;
            DateTime before, after;
            try
            {
                before = DateTime.UtcNow;
                var clock = _ops.GetSingleton(ClockHandler);
                after = DateTime.UtcNow;
                if (clock == null || !clock.TryGetValue(ClockNowKey, out var now)
                    || !WinboxValueFormat.TryToInt64(now.Value, out fetched) || fetched <= 0)
                {
                    lock (_uptimeLock) _routerClockUnreadable = true;
                    TraceRouterClockUnreadable(null);
                    return null;
                }
            }
            catch (Exception ex)
            {
                lock (_uptimeLock) _routerClockUnreadable = true;
                TraceRouterClockUnreadable(ex);
                return null;
            }

            lock (_uptimeLock)
            {
                _clockFetchedAt = before.AddTicks((after - before).Ticks / 2);
                _routerNowSeconds = fetched;
                return _routerNowSeconds;
            }
        }

        /// <summary>
        /// Seeds the boot moment a <c>relative</c> <c>dateandtime</c> is dated from, so the rendering can
        /// be pinned without a router. Nothing in the library calls it - the value is computed from the
        /// uptime singleton <c>[24,2]</c> and the timezone singleton <c>[24,0]</c>.
        /// </summary>
        internal void SeedRouterClockForTests(long bootEpochSeconds)
        {
            lock (_uptimeLock) _bootEpochHundredths = bootEpochSeconds * 100;
        }

        private static void TraceRouterClockUnreadable(Exception? ex)
        {
            if (!TikWireTrace.Enabled) return;
            TikWireTrace.Emit("wbx.codec", TikWireDir.Note,
                "the clock singleton [24,0] did not say what time the router thinks it is, so every relative "
                + "'dateandtime' field on this connection keeps its raw value"
                + (ex == null ? "" : ": " + ex.Message));
        }

        // webfig's num2int: the wire is unsigned u32, and the types that are signed reinterpret the top half
        // as negative. A value below 0x80000000 is unaffected, which is why only the types whose own
        // get/tostr calls num2int go through this.
        private static long NumToInt(long v)
            => v >= 0x80000000L && v <= 0xFFFFFFFFL ? v - 0x100000000L : v;

        private static void TraceUptimeUnreadable(Exception? ex)
        {
            if (!TikWireTrace.Enabled) return;
            TikWireTrace.Emit("wbx.codec", TikWireDir.Note,
                "the board-info singleton [24,2] gave no uptime, so every 'age' field on this connection "
                + "keeps its raw value" + (ex == null ? "" : ": " + ex.Message));
        }
    }
}
