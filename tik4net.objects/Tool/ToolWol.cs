using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace tik4net.Objects.Tool
{
    /// <summary>
    /// This tool is introduced in RouterOS since v3.23 and can send the Wake on LAN MagicPacket to any MAC address of your choosing. If the target device supports WOL, it should wake from sleep. Secure WOL is not supported.
    /// 
    /// </summary>
    public static class ToolWol
    {
        /// <summary>
        /// The WOL tool will send a UDP MagicPacket to the Broadcast address with the MAC address embedded in it.
        /// By default, the magic packet will be sent as an IP broadcast out the default gateway interface, but if you want, you can tell the command to use a specific <paramref name="iface"/>.
        /// </summary>
        /// <param name="connection">Open connection to use.</param>
        /// <param name="macAddress">MAC of the waked up device</param>
        /// <param name="iface">Optional: interface to use</param>
        public static void ExecuteWol(this ITikConnection connection, MacAddress macAddress, string? iface=null)
        {
            Guard.ArgumentNotNull(connection, "connection");
            var command = connection.CreateCommandAndParameters("/tool/wol", TikCommandParameterFormat.NameValue, "mac", macAddress.Address);
            if (!string.IsNullOrEmpty(iface))
                command.AddParameterAndValues("interface", iface!); // IsNullOrEmpty isn't NotNullWhen-annotated on netstandard2.0

            // "At most one row", because the transports genuinely disagree here (all verified live):
            //   binary API  → "!re" with no words, then "!done"  ⇒ one empty row
            //   CLI family  → the command prints nothing         ⇒ no rows
            // ExecuteSingleRow() therefore fails on the CLI transports and ExecuteNonQuery() fails on the API
            // ("Single response sentence expected" — it sees !re AND !done). OrDefault accepts both.
            command.ExecuteSingleRowOrDefault();
        }

        /// <summary>
        /// The WOL tool will send a UDP MagicPacket to the Broadcast address with the MAC address embedded in it.
        /// By default, the magic packet will be sent as an IP broadcast out the default gateway interface, but if you want, you can tell the command to use a specific <paramref name="iface"/>.
        /// </summary>
        /// <param name="connection">Open connection to use.</param>
        /// <param name="macAddress">MAC of the waked up device in format FF:FF:FF:FF:FF:FF</param>
        /// <param name="iface">Optional: interface to use</param>
        public static void ExecuteWol(this ITikConnection connection, string macAddress, string? iface = null)
        {
            var mac = new MacAddress(macAddress);

            ExecuteWol(connection, mac, iface);
        }
    }
}
