using System;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace tik4net.integrationtests
{
    /// <summary>
    /// What a read says on every transport, measured on a throwaway row: the CLI family must answer what the API answers.
    /// </summary>
    [TestClass]
    public class CliReadParityTest : TestBase
    {
        private string AddListRow(string comment)
        {
            string name = "t4n-parity-" + Guid.NewGuid().ToString("N").Substring(0, 8);
            Connection.CreateCommandAndParameters("/interface/list/add", TikCommandParameterFormat.NameValue, "name", name, "comment", comment)
                .ExecuteScalar();
            return name;
        }

        private void RemoveListRow(string name)
        {
            string id = Connection.CreateCommandAndParameters("/interface/list/print", TikCommandParameterFormat.Filter, "name", name)
                .ExecuteList().Single().GetId();
            Connection.CreateCommandAndParameters("/interface/list/remove", TikCommandParameterFormat.NameValue, ".id", id).ExecuteNonQuery();
        }

        [DataTestMethod]
        [DataRow("no such item")]
        [DataRow("failure: x")]
        [DataRow("syntax error")]
        public void ARowWhoseCommentSoundsLikeAnErrorIsARow(string comment)
        {
            string name = AddListRow(comment);
            try
            {
                var row = Connection.CreateCommandAndParameters("/interface/list/print", TikCommandParameterFormat.Filter, "name", name)
                    .ExecuteList().Single();
                Assert.AreEqual(comment, row.GetResponseField("comment"));
                Assert.AreEqual(comment, Connection.CreateCommandAndParameters("/interface/list/print", TikCommandParameterFormat.Filter, "name", name)
                    .ExecuteScalarOrDefault("(not found)", "comment"));
            }
            finally
            {
                RemoveListRow(name);
            }
        }

        [TestMethod]
        public void HasNoSuchPropertySelectsTheRowsWithNoComment()
        {
            string name = AddListRow("t4n-has-a-comment");
            try
            {
                var command = Connection.CreateCommand("/interface/list/print");
                command.AddParameter("-comment", "", TikCommandParameterFormat.Filter);
                var rows = command.ExecuteList().ToList();
                Assert.IsTrue(rows.All(r => string.IsNullOrEmpty(r.GetResponseFieldOrDefault("comment", null))), "a row with a comment came back");
                Assert.IsFalse(rows.Any(r => r.GetResponseFieldOrDefault("name", null) == name));
            }
            finally
            {
                RemoveListRow(name);
            }
        }

        [TestMethod]
        public void AGenericPrintCarriesTheFieldsTheSummaryLeavesOut()
        {
            var row = Connection.CreateCommand("/interface/print").ExecuteList()
                .First(r => r.GetResponseFieldOrDefault("type", null) == "ether");
            Assert.IsFalse(string.IsNullOrEmpty(row.GetResponseFieldOrDefault("default-name", null)), "default-name");
            Assert.IsFalse(string.IsNullOrEmpty(row.GetResponseFieldOrDefault("mtu", null)), "mtu");
        }

        /// <summary>The change-password nag of an expired password is skipped with one Ctrl-C and nothing typed into it.</summary>
        [TestMethod]
        public void AnExpiredPasswordsNagIsSkipped()
        {
            var type = ResolveConnectionType();
            if (!(Connection is tik4net.Cli.CliConnectionBase))
                Assert.Inconclusive("The nag is a terminal's.");

            string user = "t4n-exp-" + Guid.NewGuid().ToString("N").Substring(0, 8);
            const string password = "T4n-Expired-1";
            using (var api = LabSetup(TikConnectionType.Api).Create(TikConnectionType.Api))
            {
                string id = api.CreateCommandAndParameters("/user/add", TikCommandParameterFormat.NameValue, "name", user, "group", "full", "password", password).ExecuteScalar();
                try
                {
                    api.CreateCommandAndParameters("/user/expire-password", TikCommandParameterFormat.NameValue, "numbers", user).ExecuteNonQuery();
                    var setup = new TikConnectionSetup(LabAddress(type), user, password) { AllowInvalidCertificate = true };
                    using (var expired = setup.Create(type))
                        Assert.IsFalse(string.IsNullOrEmpty(expired.CreateCommand("/system/identity/print").ExecuteScalar("name")));
                }
                finally
                {
                    api.CreateCommandAndParameters("/user/remove", TikCommandParameterFormat.NameValue, ".id", id).ExecuteNonQuery();
                }
            }
        }

        /// <summary>A key landing inside RouterOS 6's repainted echo: the read still ends, and the next command reads its own answer.</summary>
        [TestMethod]
        [TestCategory(TestCategories.AnyRouter)]
        public void ACancelInsideTheEchoLeavesTheTerminalInStep()
        {
            if (!(Connection is tik4net.Cli.CliConnectionBase))
                Assert.Inconclusive("Ctrl-C is about a terminal.");

            string name = AddListRow("t4n-echo");
            try
            {
                foreach (int afterMs in new[] { 0, 5, 20, 50, 100, 200, 400 })
                {
                    var command = Connection.CreateCommandAndParameters("/interface/list/print", TikCommandParameterFormat.Filter, "name", name, "comment", "t4n-echo");
                    using (var cancel = new System.Threading.CancellationTokenSource(afterMs))
                    {
                        try { ((ITikCommandAsync)command).ExecuteListAsync(cancel.Token).GetAwaiter().GetResult(); }
                        catch (OperationCanceledException) { }
                    }
                    var row = Connection.CreateCommandAndParameters("/interface/list/print", TikCommandParameterFormat.Filter, "name", name)
                        .ExecuteList().Single();
                    Assert.AreEqual(name, row.GetResponseField("name"), "cancel after " + afterMs + " ms");
                }
            }
            finally
            {
                RemoveListRow(name);
            }
        }

        /// <summary>The whole log is read on every transport, as many rows as the API counts give or take what was logged meanwhile.</summary>
        [TestMethod]
        [TestCategory(TestCategories.AnyRouter)]
        public void AWholeLogIsRead()
        {
            var rows = Connection.CreateCommand("/log/print").ExecuteList().ToList();
            int apiCount;
            using (var api = LabSetup(TikConnectionType.Api).Create(TikConnectionType.Api))
                apiCount = api.CreateCommand("/log/print").ExecuteList().Count();
            Assert.IsTrue(Math.Abs(rows.Count - apiCount) < 100, $"{rows.Count} rows here, {apiCount} on the API");
            Assert.AreEqual(rows.Count, rows.Select(r => r.GetId()).Distinct().Count(), "a row read twice");
        }

        /// <summary>The same unformatted parameter through a scalar and through a list is the same filter.</summary>
        [TestMethod]
        public void AnUnformattedParameterOfAScalarPrintIsAFilter()
        {
            string name = AddListRow("t4n-scalar");
            try
            {
                Assert.AreEqual("t4n-scalar", Connection.CreateCommandAndParameters("/interface/list/print", "name", name).ExecuteScalar("comment"));
            }
            finally
            {
                RemoveListRow(name);
            }
        }

        [TestMethod]
        public void ASingletonAnswersAGenericPrint()
            => Assert.IsFalse(string.IsNullOrEmpty(Connection.CreateCommand("/system/identity/print").ExecuteScalar("name")));

        [TestMethod]
        public void AVerbTheLibraryHasNoShapeForAnswersItsValue()
        {
            string ids = Connection.CreateCommand("/interface/find").ExecuteScalarOrDefault(null);
            StringAssert.StartsWith(ids ?? "(none)", "*");
        }
    }
}
