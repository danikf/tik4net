using System;
using System.Collections.Generic;
using System.Configuration;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using tik4net.Objects;
using tik4net.Objects.Tool;

namespace tik4net.torch
{
    class ProgramTorch
    {
        static void Main(string[] args)
        {
            using (ITikConnection connection = ConnectionFactory.CreateConnection(TikConnectionType.Api))
            {                
                connection.Open(ConfigurationManager.AppSettings["host"], ConfigurationManager.AppSettings["user"], ConfigurationManager.AppSettings["pass"]);
                string interfaceName = ConfigurationManager.AppSettings["interface"];

                do
                {
                    var rows = connection.LoadWithDuration<ToolTorch>(3, 
                        connection.CreateParameter("interface", interfaceName),
                        //connection.CreateParameter("ip-protocol", "any"),
                        connection.CreateParameter("port", "any"),
                        connection.CreateParameter("src-address", "0.0.0.0/0"),
                        connection.CreateParameter("dst-address", "0.0.0.0/0")
                        );
        
                    Console.Clear();
                    Console.SetCursorPosition(0, 0);
                    Console.Write("Press Ctrl+C for stop.");
                    int idx = 1;
                    foreach(ToolTorch tr in rows.Where(t=>!string.IsNullOrWhiteSpace(t.SrcAddress)).OrderByDescending(t=>t.Rx).Take(10))
                    {
                        Console.SetCursorPosition(0, idx);
                        Console.Write("{0}{1} -> {2} ({3}/{4})", 
                            (tr.IpProtocol ?? "").PadRight(8), 
                            FormatAddress(tr.SrcAddress, tr.SrcPort),
                            FormatAddress(tr.DstAddress, tr.DstPort),
                            tr.Tx, tr.Rx);
                        idx++;
                    }
                    if (rows.Count() > 0)
                        Thread.Sleep(10*1000);

                } while (true);
            }
        }

        private static string FormatAddress(string ip, string port)
        {
            return (ip + ":" + port).PadRight(21);
        }
    }
}
