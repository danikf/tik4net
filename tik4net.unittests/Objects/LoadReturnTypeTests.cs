using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using tik4net.Objects;

namespace tik4net.unittests.Objects
{
    /// <summary>
    /// The mapper's list loads return a list, sync and async alike: the read is over when the call returns, and the type
    /// says so — enumerating the result again sends nothing.
    /// </summary>
    [TestClass]
    public class LoadReturnTypeTests
    {
        [DataTestMethod]
        [DataRow(typeof(TikConnectionExtensions), "LoadAll")]
        [DataRow(typeof(TikConnectionExtensions), "LoadList")]
        [DataRow(typeof(TikConnectionExtensions), "LoadWithDuration")]
        [DataRow(typeof(TikCommandExtensions), "LoadList")]
        [DataRow(typeof(TikCommandExtensions), "LoadWithDuration")]
        public void TheSyncListLoadsReturnAnIList(Type owner, string name)
        {
            var methods = owner.GetMethods(BindingFlags.Public | BindingFlags.Static).Where(m => m.Name == name).ToList();
            Assert.IsTrue(methods.Count > 0, owner.Name + "." + name + " is missing");
            foreach (var method in methods)
            {
                var returned = method.ReturnType;
                Assert.IsTrue(returned.IsGenericType && returned.GetGenericTypeDefinition() == typeof(IList<>),
                    $"{owner.Name}.{name} returns {returned.Name}, not IList<TEntity>");
            }
        }
    }
}
