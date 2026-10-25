using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Linq;

namespace tik4net.integrationtests
{
    /// <summary>
    /// <c>ExecuteNonQuery</c> on a command that prints rows runs it and discards what it printed, on every transport,
    /// as ADO.NET's <c>DbCommand.ExecuteNonQuery</c> does (command-layer X14). The connection answers the next command
    /// in step, so the discarded rows did not stay behind on the wire.
    /// </summary>
    /// <remarks>
    /// Read-only: the two commands are prints of singletons every router has. The binary API used to throw
    /// <see cref="TikCommandUnexpectedResponseException"/> ("Single response sentence expected.") after the router had
    /// run the command, and WinBox native refused a read verb with <c>NotSupportedException</c> before sending it.
    /// </remarks>
    [TestClass]
    [SafeInParallelLegs]
    public class ExecuteNonQueryDiscardsRowsTest : TestBase
    {
        private void AssertNextCommandAnswers()
        {
            var identity = Connection.CreateCommand("/system/identity/print").ExecuteList().ToList();
            Assert.AreEqual(1, identity.Count, "the next command is answered by its own row");
            Assert.IsFalse(string.IsNullOrEmpty(identity[0].GetResponseFieldOrDefault("name", null)),
                "the next command reads the identity, not a left-over row of the discarded print");
        }

        [TestMethod]
        public void ExecuteNonQuery_OfAPrint_SucceedsAndDiscardsTheRows()
        {
            Connection.CreateCommand("/system/resource/print").ExecuteNonQuery();
            AssertNextCommandAnswers();
        }

        [TestMethod]
        public void ExecuteNonQuery_OfTheIdentityPrint_SucceedsAndDiscardsTheRow()
        {
            Connection.CreateCommand("/system/identity/print").ExecuteNonQuery();
            AssertNextCommandAnswers();
        }

        [TestMethod]
        public void ExecuteNonQueryAsync_OfAPrint_SucceedsAndDiscardsTheRows()
        {
            EnsureCapability(TikConnectionCapability.AsyncCommands);
            Connection.CreateCommand("/system/resource/print").ExecuteNonQueryAsync().GetAwaiter().GetResult();
            AssertNextCommandAnswers();
        }
    }
}
