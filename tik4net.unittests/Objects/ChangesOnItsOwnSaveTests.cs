// ChangesOnItsOwnSaveTests.cs — a FullUpdate save must not write back a value the router moved by itself.
//
// FullUpdate re-reads the entity and sends every field that differs from that read. /system/clock time differs on
// every read, so saving a loaded clock after changing only its time zone sent the time it was LOADED with and set
// the router's clock back by however long the caller held the entity (found by the V2 canonical-form sweep).

using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using tik4net.Objects;
using tik4net.Objects.System;
using tik4net.Testing;

namespace tik4net.unittests.Objects
{
    [TestClass]
    public class ChangesOnItsOwnSaveTests
    {
        // Every print answers one second later than the previous one, like the router's clock.
        private static TikFakeConnection TickingClock()
        {
            int second = 0;
            return new TikFakeConnection()
                .WithResponse(
                    cmd => cmd.FirstOrDefault() == "/system/clock/print",
                    _ => new ITikSentence[]
                    {
                        new TikFakeReSentence(new Dictionary<string, string>
                        {
                            ["time"] = "12:00:" + (second++).ToString("00"), ["date"] = "2026-09-25",
                            ["time-zone-name"] = "manual", ["time-zone-autodetect"] = "false",
                            ["gmt-offset"] = "+00:00", ["dst-active"] = "false",
                        }),
                        new TikFakeDoneSentence(),
                    })
                .WithNonQuery(cmd => cmd.First() == "/system/clock/set");
        }

        private static string[] TheSet(TikFakeConnection connection)
            => connection.SentCommands.Single(c => c.First() == "/system/clock/set");

        [TestMethod]
        public void FullUpdate_DoesNotWriteBackTheTimeItLoaded()
        {
            var connection = TickingClock();
            var clock = connection.LoadSingle<SystemClock>();

            clock.TimeZoneName = "Europe/Prague";
            connection.Save(clock, saveMode: TikSaveMode.FullUpdate);

            CollectionAssert.AreEquivalent(new[] { "/system/clock/set", "=time-zone-name=Europe/Prague" }, TheSet(connection));
        }

        [TestMethod]
        public async Task FullUpdateAsync_DoesNotWriteBackTheTimeItLoaded()
        {
            var connection = TickingClock();
            var clock = connection.LoadSingle<SystemClock>();

            clock.TimeZoneName = "Europe/Prague";
            await connection.SaveAsync(clock, saveMode: TikSaveMode.FullUpdate);

            CollectionAssert.AreEquivalent(new[] { "/system/clock/set", "=time-zone-name=Europe/Prague" }, TheSet(connection));
        }

        [TestMethod]
        public void FullUpdate_StillSendsATimeTheCallerSet()
        {
            var connection = TickingClock();
            var clock = connection.LoadSingle<SystemClock>();

            clock.Time = "08:30:00";
            connection.Save(clock, saveMode: TikSaveMode.FullUpdate);

            CollectionAssert.AreEquivalent(new[] { "/system/clock/set", "=time=08:30:00" }, TheSet(connection));
        }

        [TestMethod]
        public void FullUpdate_OnAClockNeverLoaded_SendsTheTimeItHolds()
        {
            // No load, no evidence of what the caller meant: the value they put in is intent, as before.
            var connection = TickingClock();

            connection.Save(new SystemClock { Time = "08:30:00" }, saveMode: TikSaveMode.FullUpdate);

            CollectionAssert.Contains(TheSet(connection), "=time=08:30:00");
        }
    }
}
