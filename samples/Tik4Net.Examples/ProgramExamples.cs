using System;
using System.Collections.Generic;
using System.Configuration;
using System.Linq;
using System.Text;
using tik4net.Api;
using tik4net.Objects;
using tik4net.Objects.Ip;
using tik4net.Objects.Ip.Dns;
using tik4net.Objects.Ip.Firewall;
using tik4net.Objects.Queue;
using tik4net.Objects.System;

namespace tik4net.examples
{
    class ProgramExamples
    {
        static void Main(string[] args)
        {
            using (ITikConnection connection = ConnectionFactory.CreateConnection(TikConnectionType.Api))
            {
                connection.OnReadRow += Connection_OnReadRow;   // logging commands to cosole
                connection.OnWriteRow += Connection_OnWriteRow; // logging commands to cosole
                connection.Open(ConfigurationManager.AppSettings["host"], ConfigurationManager.AppSettings["user"], ConfigurationManager.AppSettings["pass"]);

                //------------------------------------------------
                //  LOW LEVEL API (hint: uncomment any example call and debug)
                Identity(connection);

                Torch(connection);

                Log(connection);

                //-------------------------------------------------
                // HIGHLEVEL API (hint: uncomment any example call and debug)

                PrintAddressList(connection);
                CreateOrUpdateAddressList(connection);
                PrintAddressList(connection);
                CreateOrUpdateAddressList(connection);
                PrintAddressList(connection);
                DeleteAddressList(connection);
                PrintAddressList(connection);

                PrintAddressList(connection);
                CreateOrUpdateAddressListMulti(connection);
                PrintAddressList(connection);
                CreateOrUpdateAddressListMulti(connection);
                PrintAddressList(connection);
                DeleteAddressListMulti(connection);
                PrintAddressList(connection);

                PrintIpAddresses(connection);

                PrintSystemResource(connection);

                ModifyIpAccounting(connection);

                AddFirewalFilter(connection);

                DhcpClientRelease(connection);

                DnsCachePrint(connection);

                //---------------------------------------------------------
                // Advanced merge support (hint: uncomment any example call and debug)
                QueueTreeMerge(connection);
                FirewallMangleMerge(connection);

                Console.WriteLine("Finito - press ENTER");
                Console.ReadLine();
            }
        }

        private static void Connection_OnWriteRow(object sender, TikConnectionCommCallbackEventArgs args)
        {
            Console.BackgroundColor = ConsoleColor.Magenta;
            Console.WriteLine(">" + args.Word);
            Console.BackgroundColor = ConsoleColor.Black;
        }

        private static void Connection_OnReadRow(object sender, TikConnectionCommCallbackEventArgs args)
        {
            Console.BackgroundColor = ConsoleColor.Green;
            Console.WriteLine("<" + args.Word);
            Console.BackgroundColor = ConsoleColor.Black;

        }

        private static void Identity(ITikConnection connection)
        {
            ITikCommand cmd = connection.CreateCommand("/system/identity/print");
            var identity = cmd.ExecuteScalar(); //cmd.ExecuteSIngleRow()
            Console.WriteLine("Identity: " + /*identity.GetResponseField("name")*/ identity);

            Console.WriteLine("Press ENTER");
            Console.ReadLine();
        }

        private static void Torch(ITikConnection connection)
        {
            ITikCommand torchCmd = connection.CreateCommand("/tool/torch", 
                connection.CreateParameter("interface", "ether1"), 
                connection.CreateParameter("port", "any"),
                connection.CreateParameter("src-address", "0.0.0.0/0"),
                connection.CreateParameter("dst-address", "0.0.0.0/0")
                );
            torchCmd.ExecuteAsync(response =>
            {
                Console.WriteLine("Row: " + response.GetResponseField("tx"));
            });
            Console.WriteLine("Press ENTER");
            Console.ReadLine();
            torchCmd.CancelAndJoin();
        }

        private static void Log(ITikConnection connection)
        {
            var logs = connection.LoadList<Log>();
            foreach (Log log in logs)
            {
                Console.WriteLine("{0}[{1}]: {2}", log.Time, log.Topics, log.Message);
            }
        }


        const string listName = "TEST_LIST";
        const string ipAddress = "192.168.1.1";
        const string ipAddress2 = "192.168.1.2";
        private static void PrintAddressList(ITikConnection connection)
        {
            var addressLists = connection.LoadList<FirewallAddressList>(
                connection.CreateParameter("list", listName));
            foreach (FirewallAddressList addressList in addressLists)
            {
                Console.WriteLine("{0}{1}: {2} {3} ({4})", addressList.Disabled ? "X" : " ", addressList.Dynamic ? "D" : " ", addressList.Address, addressList.List, addressList.Comment);
            }
        }

        private static void CreateOrUpdateAddressList(ITikConnection connection)
        {
            var existingAddressList = connection.LoadList<FirewallAddressList>(
                connection.CreateParameter("list", listName),
                connection.CreateParameter("address", ipAddress)).SingleOrDefault();
            if (existingAddressList == null)
            {
                //Create
                var newAddressList = new FirewallAddressList()
                {
                    Address = ipAddress,
                    List = listName,
                };
                connection.Save(newAddressList);
            }
            else
            {
                //Update
                existingAddressList.Comment = "Comment update: " + DateTime.Now.ToShortTimeString();

                connection.Save(existingAddressList);
            }
        }

        private static void DeleteAddressList(ITikConnection connection)
        {
            var existingAddressList = connection.LoadList<FirewallAddressList>(
                connection.CreateParameter("list", listName),
                connection.CreateParameter("address", ipAddress)).SingleOrDefault();

            if (existingAddressList != null)
                connection.Delete(existingAddressList);
        }

        private static void CreateOrUpdateAddressListMulti(ITikConnection connection)
        {
            var existingAddressList = connection.LoadList<FirewallAddressList>(
                connection.CreateParameter("list", listName)).ToList();
            var listClonedBackup = existingAddressList.CloneEntityList(); //creates clone of all entities in list

            if (existingAddressList.Count() <= 0)
            {
                //Create (just in memory)
                existingAddressList.Add(
                    new FirewallAddressList()
                    {
                        Address = ipAddress,
                        List = listName,
                    });
                existingAddressList.Add(
                    new FirewallAddressList()
                    {
                        Address = ipAddress2,
                        List = listName,
                    });
            }
            else
            {
                //Update (just in memory)
                foreach (var addressList in existingAddressList)
                {
                    addressList.Comment = "Comment update: " + DateTime.Now.ToShortTimeString();
                }
            }

            //save differences into mikrotik  (existingAddressList=modified, listClonedBackup=unmodified)
            connection.SaveListDifferences(existingAddressList, listClonedBackup);
        }

        private static void DeleteAddressListMulti(ITikConnection connection)
        {
            var existingAddressList = connection.LoadList<FirewallAddressList>(
                connection.CreateParameter("list", listName)).ToList();
            var listClonedBackup = existingAddressList.CloneEntityList(); //creates clone of all entities in list

            existingAddressList.Clear();

            //save differences into mikrotik  (existingAddressList=modified, listClonedBackup=unmodified)
            connection.SaveListDifferences(existingAddressList, listClonedBackup);
        }

        private static void PrintIpAddresses(ITikConnection connection)
        {
            var ipAddresses = connection.LoadList<IpAddress>();
            foreach(var addr in ipAddresses)
            {
                Console.WriteLine(addr.EntityToString());
            }
        }

        private static void PrintSystemResource(ITikConnection connection)
        {
            var sysRes = connection.LoadSingle<SystemResource>();
            Console.WriteLine(sysRes.EntityToString());
        }

        private static void QueueTreeMerge(ITikConnection connection)
        {
            var original = connection.LoadAll<QueueTree>().Where(q=> q.Name == "Q1" || q.Name == "Q2" || q.Name.StartsWith("Q3"));

            string unique = Guid.NewGuid().ToString();
            List<QueueTree> expected = new List<QueueTree>()
            {
                new QueueTree() { Name = "Q1", Parent = "global", PacketMark = "PM1" },
                new QueueTree() { Name = "Q2", Parent = "global", PacketMark = "PM2", Comment = unique }, //always update
                new QueueTree() { Name = "Q3 " + unique, Parent = "global", PacketMark = "PM3" }, // always insert + delete from previous run
            };

            //Merge with Name as key - can not save via SaveListDifferences because all items in 'expected' are new (.id=null) => insert will be done, not CUD
            connection.CreateMerge(expected, original)            
                .WithKey(queue => queue.Name)
                .Field(q => q.Parent)
                .Field(q => q.PacketMark)
                .Field(q => q.Comment)
                .Save();
        }

        private static void FirewallMangleMerge(ITikConnection connection)
        {
            //manage just subset before rules marked with comment =START= and =END=

            //Create subset boundaries if not present
            const string startComment = "=START=";
            const string endComment = "=END=";
            var startMangle = connection.LoadSingleOrDefault<FirewallMangle>(connection.CreateParameter("comment", startComment));
            if (startMangle == null)
            {
                startMangle = new FirewallMangle()
                {
                    Chain = "forward",
                    Action = FirewallMangle.ActionType.Passthrough,
                    Comment = startComment,
                    Disabled = true,
                };
                connection.Save(startMangle);
            };
            var endMangle = connection.LoadSingleOrDefault<FirewallMangle>(connection.CreateParameter("comment", endComment));
            if (endMangle == null)
            {
                endMangle = new FirewallMangle()
                {
                    Chain = "forward",
                    Action = FirewallMangle.ActionType.Passthrough,
                    Comment = endComment,
                    Disabled = true,
                };
                connection.Save(endMangle);
            };

            //Merge subset between boundaries
            string unique = Guid.NewGuid().ToString();
            List<FirewallMangle> original = connection.LoadAll<FirewallMangle>().SkipWhile(m=>m.Comment != startComment).TakeWhile(m=>m.Comment != endComment)
                .Concat(new List<FirewallMangle> { endMangle})
                .ToList(); //just subset between =START= and =END= (not very elegant but functional and short ;-) )
            List<FirewallMangle> expected = new List<FirewallMangle>();
            expected.Add(startMangle);
            expected.Add(new FirewallMangle()
            {
                Chain = "forward",
                SrcAddress = "192.168.1.1",
                Action = FirewallMangle.ActionType.MarkPacket,
                NewPacketMark = "mark-001",
                Passthrough = false,
            });
            expected.Add(new FirewallMangle()
            {
                Chain = "forward",
                SrcAddress = "192.168.1.2",
                Action = FirewallMangle.ActionType.MarkPacket,
                NewPacketMark = "mark-002" + "-" +  unique,
                Passthrough = false,
            });
            expected.Add(new FirewallMangle()
            {
                Chain = "forward",
                SrcAddress = "192.168.1.3",
                Action = FirewallMangle.ActionType.MarkPacket,
                NewPacketMark = "mark-003",
                Passthrough = false,
                Comment = unique,
            });
            expected.Add(endMangle);

            connection.CreateMerge(expected, original)
                .WithKey(mangle => mangle.SrcAddress + ":" + mangle.Comment) //Use src-address as key
                .Field(q => q.Chain) 
                .Field(q => q.SrcAddress) //Do not forget include also key fields !!!
                .Field(q => q.Action)
                .Field(q => q.NewPacketMark)
                .Field(q => q.Passthrough)
                .Field(q => q.Comment)
                .Save();
        }
        private static void ModifyIpAccounting(ITikConnection connection)
        {
            var accounting = connection.LoadSingle<IpAccounting>();
            accounting.Threshold = 257;
            connection.Save(accounting);
        }

        private static void AddFirewalFilter(ITikConnection connection)
        {
            var firewallFilter = new FirewallFilter()
            {
                Chain = FirewallFilter.ChainType.Forward,
                Action = FirewallFilter.ActionType.Accept,
            };

            connection.Save(firewallFilter);

            var loaded = connection.LoadAll<FirewallFilter>().First();
            loaded.Comment = "TEST";
            connection.Save(loaded);
        }

        private static void DhcpClientRelease(ITikConnection connection)
        {
            connection.LoadAll<IpDhcpClient>().First().Release(connection);
        }

        private static void DnsCachePrint(ITikConnection connection)
        {
            var cache = connection.LoadAll<DnsCache>();
            foreach(var c in cache)
            {
                Console.WriteLine(c.EntityToString());
            }
        }
    }
}
