using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using tik4net.Objects;
using tik4net.Objects.Tracking;
using tik4net.Testing;

namespace tik4net.unittests.Objects
{
    /// <summary>
    /// The sync mapper entry points against their async twins: each refuses a null connection by name, as the twin
    /// does, and <c>LoadWithDuration</c> tracks its rows as every other connection-level load does.
    /// </summary>
    [TestClass]
    public class MapperLoadGuardTests
    {
        [TikEntity("/probe/guard", IsOrdered = true)]
        public class GuardRow
        {
            [TikProperty(".id", IsReadOnly = true, IsMandatory = true)]
            public string Id { get; set; }

            [TikProperty("name")]
            public string Name { get; set; }
        }

        private static GuardRow Row(string id, string name) => new GuardRow { Id = id, Name = name };

        // Every public sync method of TikConnectionExtensions whose async twin guards the connection (DeleteAll's twin
        // guards through LoadAllAsync). ExecuteNonQuery / ExecuteScalar have no twin there.
        [DataTestMethod]
        [DataRow("LoadAll")]
        [DataRow("LoadList")]
        [DataRow("LoadSingle")]
        [DataRow("LoadSingleOrDefault")]
        [DataRow("LoadById")]
        [DataRow("LoadByName")]
        [DataRow("Save")]
        [DataRow("Delete")]
        [DataRow("DeleteAll")]
        [DataRow("SaveListDifferences")]
        [DataRow("Move")]
        [DataRow("MoveToEnd")]
        public void ANullConnectionIsRefusedByName(string method)
        {
            ITikConnection connection = null!;
            Action call = method switch
            {
                "LoadAll" => () => connection.LoadAll<GuardRow>(),
                "LoadList" => () => connection.LoadList<GuardRow>(),
                "LoadSingle" => () => connection.LoadSingle<GuardRow>(),
                "LoadSingleOrDefault" => () => connection.LoadSingleOrDefault<GuardRow>(),
                "LoadById" => () => connection.LoadById<GuardRow>("*1"),
                "LoadByName" => () => connection.LoadByName<GuardRow>("a"),
                "Save" => () => connection.Save(Row(null, "a")),
                "Delete" => () => connection.Delete(Row("*1", "a")),
                "DeleteAll" => () => connection.DeleteAll<GuardRow>(),
                "SaveListDifferences" => () => connection.SaveListDifferences(new[] { Row(null, "n") }, new[] { Row("*1", "a") }),
                "Move" => () => connection.Move(Row("*1", "a"), Row("*2", "b")),
                "MoveToEnd" => () => connection.MoveToEnd(Row("*1", "a")),
                _ => throw new ArgumentOutOfRangeException(nameof(method)),
            };

            var ex = Assert.ThrowsException<ArgumentNullException>(call, method);
            Assert.AreEqual("connection", ex.ParamName, method);
        }

        [TestMethod]
        public void LoadWithDurationTakesASnapshot()
        {
            var connection = new TikFakeConnection().WithEntities(Row("*1", "a"), Row("*2", "b"));

            var rows = connection.LoadWithDuration<GuardRow>(TimeSpan.FromSeconds(1)).ToList();

            Assert.AreEqual(2, rows.Count);
            foreach (var row in rows)
                Assert.IsNotNull(TikChangeTracker.For(connection).GetSnapshot(row), row.Id + " is not tracked");
        }
    }
}
