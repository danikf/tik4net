using System;
using System.IO;
using System.Reflection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
#if NET
using System.Runtime.Loader;
#endif

namespace tik4net.unittests.Connection
{
    /// <summary>
    /// <c>TikConnectionType.Ssh</c> is the one transport in a satellite package. Referencing <c>tik4net.ssh</c> is
    /// enough to create it: the registry loads the package itself the first time the slot is asked for. Without the
    /// package, the refusal is a configuration mistake (<see cref="InvalidOperationException"/>) whose message names the
    /// package and the two ways to reach the transport.
    /// </summary>
    /// <remarks>
    /// Every other test class in this process calls <c>Tik4NetSsh.Register()</c>, so the registry here is never empty.
    /// The two cases that need an empty one load <c>tik4net</c> into a fresh <c>AssemblyLoadContext</c>, which has a
    /// registry of its own — one that can also be denied the satellite.
    /// </remarks>
    [TestClass]
    public class SshSelfRegistrationTests
    {
        [TestMethod]
        public void AnUndeclaredTypeIsAConfigurationMistake_NotAnUnfinishedLibrary()
        {
            var ex = Assert.ThrowsException<InvalidOperationException>(
                () => new TikConnectionSetup("192.0.2.1", "u", "p").CreateUnopened((TikConnectionType)9999));
            StringAssert.Contains(ex.Message, "9999");
        }

#if NET
        [TestMethod]
        public void WithThePackageReferenced_SshIsCreatedWithoutRegister()
        {
            var context = new IsolatedTik4Net(allowSsh: true);
            try
            {
                var connection = context.CreateConnection("Ssh");
                Assert.AreEqual("tik4net.Ssh.SshConnection", connection.GetType().FullName);
                ((IDisposable)connection).Dispose();
            }
            finally
            {
                context.Unload();
            }
        }

        [TestMethod]
        public void WithoutThePackage_TheRefusalNamesItAndTheOneLineFix()
        {
            var context = new IsolatedTik4Net(allowSsh: false);
            try
            {
                var ex = Assert.ThrowsException<InvalidOperationException>(() => context.CreateConnection("Ssh"));
                StringAssert.Contains(ex.Message, "dotnet add package tik4net.ssh");
                StringAssert.Contains(ex.Message, "Tik4NetSsh.Register()");
                StringAssert.Contains(ex.Message, "CreateSshConnection()");
            }
            finally
            {
                context.Unload();
            }
        }

        // tik4net (and, when allowed, tik4net.ssh) loaded afresh: a registry nothing has registered into yet.
        private sealed class IsolatedTik4Net : AssemblyLoadContext
        {
            private static readonly string Tik4NetPath = typeof(TikConnectionSetup).Assembly.Location;
            private static readonly string SshPath = typeof(tik4net.Ssh.Tik4NetSsh).Assembly.Location;
            private readonly bool _allowSsh;
            private readonly Assembly _tik4net;

            public IsolatedTik4Net(bool allowSsh) : base(isCollectible: true)
            {
                _allowSsh = allowSsh;
                _tik4net = LoadFromAssemblyPath(Tik4NetPath);
            }

            protected override Assembly Load(AssemblyName assemblyName)
            {
                if (assemblyName.Name == "tik4net")
                    return _tik4net;
                if (assemblyName.Name == "tik4net.ssh")
                    return _allowSsh
                        ? LoadFromAssemblyPath(SshPath)
                        : throw new FileNotFoundException("denied to this test", "tik4net.ssh");
                return null;   // everything else from the default context
            }

            public object CreateConnection(string connectionType)
            {
                var factory = _tik4net.GetType("tik4net.ConnectionFactory", throwOnError: true)!;
                var enumType = _tik4net.GetType("tik4net.TikConnectionType", throwOnError: true)!;
                var create = factory.GetMethod("CreateConnection", new[] { enumType })!;
                try
                {
                    return create.Invoke(null, new[] { Enum.Parse(enumType, connectionType) })!;
                }
                catch (TargetInvocationException ex) when (ex.InnerException != null)
                {
                    throw ex.InnerException;
                }
            }
        }
#endif
    }
}
