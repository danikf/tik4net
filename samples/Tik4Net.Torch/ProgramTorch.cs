using System;
using System.Collections.Generic;
using System.Configuration;
using System.Linq;
using System.Text;
using System.Threading;
using tik4net.Objects;
using tik4net.Objects.Tool;

namespace tik4net.torch
{
    class ProgramTorch
    {
        static void Main(string[] args)
        {
            //Future idea: create tool like iftop via API
            //Posible features:
            // DNS resolve
            // Geolocation - https://db-ip.com/db/
            // Visualise if address is from any AddressList
            // ??
            // looking for volunteers :-)

            using (ITikConnection connection = ConnectionFactory.CreateConnection(TikConnectionType.Api))
            {                
                connection.Open(ConfigurationManager.AppSettings["host"], ConfigurationManager.AppSettings["user"], ConfigurationManager.AppSettings["pass"]);
                string interfaceName = ConfigurationManager.AppSettings["interface"];

                Console.Clear();

                var loadingContext = connection.LoadAsync<ToolTorch>(
                    TorchItemRead, error => Console.WriteLine(error.ToString()),                                                
                    connection.CreateParameter("interface", interfaceName),
                    //connection.CreateParameter("ip-protocol", "any"),
                    connection.CreateParameter("port", "any"),
                    connection.CreateParameter("src-address", "0.0.0.0/0"),
                    connection.CreateParameter("dst-address", "0.0.0.0/0"));

                Console.ReadLine();

                loadingContext.Cancel();
            }
        }

        private static string FormatAddress(string ip, string port)
        {
            return (ip + ":" + port).PadRight(21);
        }

        private static void TorchItemRead(ToolTorch item)
        {
            Console.WriteLine("{0}{1} -> {2} ({3}/{4})",
                (item.IpProtocol ?? "").PadRight(8),
                FormatAddress(item.SrcAddress, item.SrcPort),
                FormatAddress(item.DstAddress, item.DstPort),
                item.Tx, item.Rx);
        }
    }
}
