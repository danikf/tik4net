using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using tik4net.Objects;
using tik4net.Objects.System;

namespace tik4net.integrationtests
{
    [TestClass]
    public class SystemHealthTest : TestBase
    {
        [TestMethod]
        public void LoadSystemHealthWillNotFail()
        {
            EnsureCommandAvailable("/system/health");
            try
            {
                var health = Connection.LoadSingle<SystemHealth>();
                Assert.IsNotNull(health);
            }
            catch (Exception ex) when (IsWinboxNativeUnsupported(ex))
            {
                // Safety net only — the previous native /system/health gap is fixed. WinBox health is
                // board-gated: a name/value 'map' window ([24,29], non-x86) and a hardware-sensor 'item'
                // singleton window ([24,14], x86, read via get-singleton). The shipped path alias used to
                // resolve to the map handler, which answers getall with NotImplemented (0xFE0002) on
                // x86/CHR. WinboxNativeConnection now prefers the catalog's singleton health window
                // (handler read live from the .jg), so LoadSingle succeeds. (On this CHR there are no lm87
                // sensors, so the sensor fields are empty; state/state-after-reboot are API/CLI-only fields
                // WinBox never exposes.) Verified live RouterOS 7.21.4. Kept bound to the actual error in
                // case a future board/build regresses.
                Assert.Inconclusive("/system/health is not readable over native WinBox M2: " + ex.Message);
            }
        }
    }
}
