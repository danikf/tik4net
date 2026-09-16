using System;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using tik4net;
using tik4net.Api;
using tik4net.Ssh;

namespace tik4net.unittests.Connection
{
    /// <summary>
    /// <see cref="ConnectionFactory.RegisterConnectionFactory"/> can only ever take effect for a type core does
    /// not implement, so every other registration is refused rather than accepted and ignored.
    /// </summary>
    /// <remarks>
    /// <see cref="RegisteringABuiltInTypeIsRefused"/> and <see cref="RegisteringAnUndeclaredTypeIsRefused"/>
    /// were red on the old code: it stored the factory and returned, and the built-in type kept answering.
    /// </remarks>
    [TestClass]
    public class ConnectionFactoryRegistrationTests
    {
        [TestMethod]
        public void RegisteringABuiltInTypeIsRefused()
        {
            var builtIn = Enum.GetValues(typeof(TikConnectionType)).Cast<TikConnectionType>()
                .Where(t => t != TikConnectionType.Ssh).ToList();
            Assert.AreEqual(10, builtIn.Count, "a new TikConnectionType value: is it built in or a satellite?");

            foreach (var type in builtIn)
            {
                var ex = Assert.ThrowsException<ArgumentException>(
                    () => ConnectionFactory.RegisterConnectionFactory(type, () => throw new AssertFailedException("never called")),
                    type.ToString());
                StringAssert.Contains(ex.Message, type.ToString());
                Assert.AreEqual("connectionType", ex.ParamName);
            }
        }

        [TestMethod]
        public void ARefusedRegistrationLeavesTheBuiltInTypeAnswering()
        {
            Assert.ThrowsException<ArgumentException>(
                () => ConnectionFactory.RegisterConnectionFactory(TikConnectionType.Api, () => throw new AssertFailedException("never called")));
            using (var conn = ConnectionFactory.CreateConnection(TikConnectionType.Api))
                Assert.IsInstanceOfType(conn, typeof(ApiConnection));
        }

        [TestMethod]
        public void RegisteringAnUndeclaredTypeIsRefused()
        {
            Assert.ThrowsException<ArgumentException>(
                () => ConnectionFactory.RegisterConnectionFactory((TikConnectionType)9999, () => throw new AssertFailedException("never called")));
        }

        [TestMethod]
        public void TheSatelliteTypeCanBeRegistered()
        {
            Tik4NetSsh.Register();
            using (var conn = ConnectionFactory.CreateConnection(TikConnectionType.Ssh))
                Assert.IsInstanceOfType(conn, typeof(SshConnection));
        }

        [TestMethod]
        public void ANullFactoryIsRefused()
        {
            Assert.ThrowsException<ArgumentNullException>(
                () => ConnectionFactory.RegisterConnectionFactory(TikConnectionType.Ssh, null));
        }
    }
}
