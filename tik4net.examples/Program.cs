using System;
using System.Collections.Generic;
using System.Configuration;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using tik4net.Api;
using tik4net.Objects;

namespace tik4net.examples
{
    class Program
    {
        static void Main(string[] args)
        {
            using (ITikConnection connection = new ApiConnection())
            {
                connection.OnReadRow += Connection_OnReadRow;
                connection.OnWriteRow += Connection_OnWriteRow;
                connection.Open(ConfigurationManager.AppSettings["host"], ConfigurationManager.AppSettings["user"], ConfigurationManager.AppSettings["pass"]);

                //Identity(connection);
                //Torch(connection);
                Log(connection);

                Console.WriteLine("Finito - press ENTER");
                Console.ReadLine();
            }
        }

        private static void Connection_OnWriteRow(object sender, string word)
        {
            Console.BackgroundColor = ConsoleColor.Magenta;
            Console.WriteLine(">" + word);
            Console.BackgroundColor = ConsoleColor.Black;
        }

        private static void Connection_OnReadRow(object sender, string word)
        {
            Console.BackgroundColor = ConsoleColor.Green;
            Console.WriteLine("<" + word);
            Console.BackgroundColor = ConsoleColor.Black;

        }

        private static void Identity(ITikConnection connection)
        {
            ApiCommand cmd = new ApiCommand(connection, "/system/identity/print");
            var identity = cmd.ExecuteSingleRow();
            Console.WriteLine("Identity: " + identity.GetResponseField("name"));

            Console.WriteLine("Press ENTER");
            Console.ReadLine();
        }

        private static void Torch(ITikConnection connection)
        {
            ApiCommand torchCmd = new ApiCommand(connection, "/tool/torch", new ApiCommandParameter("interface", "ether1"));
            torchCmd.ExecuteAsync(response =>
            {
                Console.WriteLine("Row: " + response.GetResponseField("tx"));
            });
            Console.WriteLine("Press ENTER");
            Console.ReadLine();
            torchCmd.Cancel();
        }

        private static void Log(ITikConnection connection)
        {
            var logs = connection.LoadList<Log>();
            foreach(Log log in logs)
            {
                Console.WriteLine("{0}[{1}]: {2}", log.Time, log.Topics, log.Message);
            }
        }
    }
}
