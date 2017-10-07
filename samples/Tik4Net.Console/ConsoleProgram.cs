using System;
using System.Collections.Generic;
using System.Configuration;
using System.Linq;
using System.Text;
using tik4net.Api;

namespace tik4net.console
{
    class ConsoleProgram
    {
        static void Main(string[] args)
        {
            using (ITikConnection connection = ConnectionFactory.CreateConnection(TikConnectionType.Api))
            {
                connection.OnReadRow += Connection_OnReadRow;
                connection.OnWriteRow += Connection_OnWriteRow;
                connection.Open(ConfigurationManager.AppSettings["host"], ConfigurationManager.AppSettings["user"], ConfigurationManager.AppSettings["pass"]);

                List<string> commandRows = new List<string>();
                string command;
                Console.WriteLine("Write command and press [ENTER] on empty line");
                Console.WriteLine("Empty command + [ENTER] stops console.");
                do
                {
                    command = Console.ReadLine();

                    if (!string.IsNullOrWhiteSpace(command))
                        commandRows.Add(command);
                    else
                    {
                        if (commandRows.Any())
                        {
                            List<string> rows = new List<string>();
                            foreach(string row in commandRows)
                            {
                                rows.AddRange(row.Split('|').Where(r => !string.IsNullOrEmpty(r)));
                            }
                            var result = connection.CallCommandSync(rows.ToArray());
                            foreach (var resultItem in result)
                                Console.WriteLine(resultItem.Words);

                            commandRows.Clear();
                        }
                        else
                        {
                            break; //empty row and empty command -> end
                        }

                    }
                        
                }
                while (true);
            }

            Console.WriteLine("Press [ENTER] to close.");
            Console.ReadLine();
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
    }
}