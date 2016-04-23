using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace tik4net.tests
{
    [TestClass]
    public class ConnectionTest : TestBase
    {
        [TestMethod]
        public void OpenConnectionWillNotFail()
        {
            Connection.Close();
        }

        [TestMethod]
        public void ConnectionEncodingWorksCorrectly()
        {
            Connection.Encoding = Encoding.GetEncoding("windows-1250");
            ITikCommand readCmd = Connection.CreateCommand("/system/identity/print");
            var originalIdentity = readCmd.ExecuteScalar();

            //modify
            const string testStringWithExoticCharacters = "Příliš žluťoučký kůň úpěl ďábelské ódy.";
            ITikCommand setCmd = Connection.CreateCommand("/system/identity/set");
            setCmd.AddParameterAndValues("name", testStringWithExoticCharacters);
            setCmd.ExecuteNonQuery();

            //read modified
            var newIdentity = readCmd.ExecuteScalar();
            Assert.AreEqual(testStringWithExoticCharacters, newIdentity);

            //cleanup
            setCmd.Parameters.Clear();
            setCmd.AddParameterAndValues("name", originalIdentity);
            setCmd.ExecuteNonQuery();
        }

    }
}
