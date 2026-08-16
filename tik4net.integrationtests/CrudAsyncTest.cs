using System.Linq;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using tik4net.Objects;

namespace tik4net.integrationtests
{
    /// <summary>
    /// The Task-based O/R mapper (A7) against a live router: <c>LoadAllAsync</c> / <c>LoadByIdAsync</c> /
    /// <c>SaveAsync</c> / <c>DeleteAsync</c>, on every transport that declares
    /// <see cref="TikConnectionCapability.AsyncCommands"/> — which today is all of them.
    /// </summary>
    /// <remarks>
    /// The unit suite already pins that each of these sends exactly what its synchronous twin sends
    /// (<c>MapperAsyncEquivalenceTests</c>, against a fake connection). What only a router can answer is
    /// whether the resulting conversation survives each transport's own translation of it — the CLI
    /// transports rebuild every command as terminal text, and WinBox native as M2 messages.
    /// </remarks>
    [TestClass]
    public class CrudAsyncTest : TestBase
    {
        private async Task CleanupByAddressAsync(string ip)
        {
            var existing = await Connection.LoadListAsync<Objects.Ip.IpAddress>(
                filterParameters: Connection.CreateParameter("address", ip));
            foreach (var address in existing)
                await Connection.DeleteAsync(address);
        }

        [TestMethod]
        public async Task LoadAllAsync_WillNotFail()
        {
            EnsureCapability(TikConnectionCapability.AsyncCommands, "Task-based O/R mapper");

            var interfaces = await Connection.LoadAllAsync<Objects.Interface.Interface>();

            Assert.IsNotNull(interfaces);
            Assert.IsTrue(interfaces.Count > 0, "the router must report at least one interface");
        }

        [TestMethod]
        public async Task SaveAsync_CreatesUpdatesAndDeletes()
        {
            EnsureCapability(TikConnectionCapability.AsyncCommands, "Task-based O/R mapper");

            string ip = TestConstants.Address;
            await CleanupByAddressAsync(ip);

            var entity = new Objects.Ip.IpAddress
            {
                Address = ip,
                Interface = TestConstants.Interface,
            };

            try
            {
                await Connection.SaveAsync(entity);
                Assert.IsFalse(string.IsNullOrEmpty(entity.Id), "SaveAsync must write the new .id back into the entity");

                // Reload by the id the create reported, then change one field and save again. The update
                // path is the one with rules of its own (what is sent, and whether anything is sent at all),
                // so it is worth a round trip rather than trusting the create alone.
                var loaded = await Connection.LoadByIdAsync<Objects.Ip.IpAddress>(entity.Id);
                Assert.AreEqual(ip, loaded.Address);

                loaded.Comment = "t4n-async-crud";
                await Connection.SaveAsync(loaded);

                var reloaded = await Connection.LoadByIdAsync<Objects.Ip.IpAddress>(entity.Id);
                Assert.AreEqual("t4n-async-crud", reloaded.Comment);

                await Connection.DeleteAsync(reloaded);

                var afterDelete = await Connection.LoadListAsync<Objects.Ip.IpAddress>(
                    filterParameters: Connection.CreateParameter("address", ip));
                Assert.AreEqual(0, afterDelete.Count, "DeleteAsync must remove the row");
                entity = null;
            }
            finally
            {
                // A failure part-way through leaves the address behind, and an orphan here changes the error
                // the next transport in the matrix sees.
                if (entity != null)
                    await CleanupByAddressAsync(ip);
            }
        }
    }
}
