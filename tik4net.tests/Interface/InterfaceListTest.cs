using System;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using tik4net.Objects;
using tik4net.Objects.Interface;

namespace tik4net.tests
{
    [TestClass]
    public class InterfaceListTest : TestBase
    {
        // --- /interface/list ---------------------------------------------------

        [TestMethod]
        public void ListInterfaceListsWillNotFail()
        {
            EnsureCommandAvailable("/interface/list");
            var list = Connection.LoadAll<InterfaceList>();
            Assert.IsNotNull(list);
        }

        [TestMethod]
        public void AddInterfaceListWillNotFail()
        {
            EnsureCommandAvailable("/interface/list");
            string marker = Guid.NewGuid().ToString();
            var entity = new InterfaceList
            {
                Name = "t4n-test-" + Guid.NewGuid().ToString("N").Substring(0, 8),
                Comment = marker,
            };
            Connection.Save(entity);

            var loaded = Connection.LoadById<InterfaceList>(entity.Id);
            Assert.IsNotNull(loaded);
            Assert.AreEqual(marker, loaded.Comment);
            Assert.IsFalse(loaded.Builtin);

            Connection.Delete(loaded);
        }

        // --- /interface/list/member -------------------------------------------

        [TestMethod]
        public void ListInterfaceListMembersWillNotFail()
        {
            EnsureCommandAvailable("/interface/list/member");
            var list = Connection.LoadAll<InterfaceListMember>();
            Assert.IsNotNull(list);
        }

        [TestMethod]
        public void AddInterfaceListMemberWillNotFail()
        {
            EnsureCommandAvailable("/interface/list/member");

            // Members can only be added to non-builtin lists, so create a throwaway list first.
            var parentList = new InterfaceList
            {
                Name = "t4n-test-" + Guid.NewGuid().ToString("N").Substring(0, 8),
                Comment = "tik4net member test",
            };
            Connection.Save(parentList);
            try
            {
                string interfaceName = Connection.LoadAll<tik4net.Objects.Interface.Interface>().First().Name;
                string marker = Guid.NewGuid().ToString();
                var member = new InterfaceListMember
                {
                    List = parentList.Name,
                    Interface = interfaceName,
                    Comment = marker,
                };
                Connection.Save(member);

                var loaded = Connection.LoadById<InterfaceListMember>(member.Id);
                Assert.IsNotNull(loaded);
                Assert.AreEqual(marker, loaded.Comment);
                Assert.AreEqual(parentList.Name, loaded.List);
                Assert.AreEqual(interfaceName, loaded.Interface);

                Connection.Delete(loaded);
            }
            finally
            {
                Connection.Delete(parentList);
            }
        }
    }
}
