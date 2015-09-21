//using System;
//using System.Collections.Generic;
//using System.Linq;
//using System.Text;
//using System.Text.RegularExpressions;

//namespace tik4net.Objects
//{
//    /// <summary>
//    /// MAC address in format 00:00:00:00:00:00
//    /// </summary>
//    public class MacAddress
//    {
//        private const string DEFAULT_MAC = "00:00:00:00:00:00";
//        private string _address;

//        /// <summary>
//        /// MAC as string in format 00:00:00:00:00:00.
//        /// </summary>
//        public static implicit operator string (MacAddress mac) { return mac == null ? DEFAULT_MAC : mac.Address; }

//        /// <summary>
//        /// MAC as string in format 00:00:00:00:00:00.
//        /// </summary>
//        public static implicit operator MacAddress(string mac)
//        {
//            return new MacAddress(mac);
//        }

//        /// <summary>
//        /// MAC address in format 00:00:00:00:00:00
//        /// </summary>
//        public string Address
//        {
//            get { return _address; }
//            set
//            {
//                EnsureValidMac(value);
//                _address = value;
//            }
//        }

//        /// <summary>
//        /// Ctor - 00:00:00:00:00:00
//        /// </summary>
//        public MacAddress()
//        {
//            _address = DEFAULT_MAC;
//        }

//        /// <summary>
//        /// Ctor
//        /// </summary>
//        /// <param name="mac">MAC address in format 00:00:00:00:00:00</param>
//        public MacAddress(string mac)
//        {
//            if (StringHelper.IsNullOrWhiteSpace(mac))
//                mac = DEFAULT_MAC;

//            EnsureValidMac(mac);
//            _address = mac;
//        }

//        private static Regex MacRegex = new Regex(@"^[\da-f]{2}:[\da-f]{2}:[\da-f]{2}:[\da-f]{2}:[\da-f]{2}:[\da-f]{2}$", RegexOptions.IgnoreCase);
//        private static void EnsureValidMac(string mac)
//        {
//            if (!MacRegex.IsMatch(mac))
//                throw new ArgumentException(string.Format("Invalid MAC address '{0}'.", mac));
//        }
//    }
//}
