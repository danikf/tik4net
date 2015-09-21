//using System;
//using System.Collections.Generic;
//using System.Linq;
//using System.Text;
//using System.Text.RegularExpressions;

//namespace tik4net.Objects
//{
//    /// <summary>
//    /// IP address in format 192.168.1.1
//    /// </summary>
//    public class Ipv4Address
//    {
//        private const string DEFAULT_IP = "0.0.0.0";
//        private string _address;

//        /// <summary>
//        /// IP address as string in format 192.168.1.1
//        /// </summary>
//        public static implicit operator string (Ipv4Address ip) { return ip == null ? DEFAULT_IP : ip.Address; }

//        /// <summary>
//        /// IP address as string in format 192.168.1.1
//        /// </summary>
//        public static implicit operator Ipv4Address(string ip)
//        {
//            return new Ipv4Address(ip);
//        }

//        /// <summary>
//        /// IP address as string in format 192.168.1.1
//        /// </summary>
//        public string Address
//        {
//            get { return _address; }
//            set
//            {
//                EnsureValidIp(value);
//                _address = value;
//            }
//        }

//        /// <summary>
//        /// IpAddress - 0.0.0.0
//        /// </summary>
//        public Ipv4Address()
//        {
//            _address = DEFAULT_IP;
//        }

//        /// <summary>
//        /// Ctor
//        /// </summary>
//        /// <param name="ip">IP address in format 192.168.1.1</param>
//        public Ipv4Address(string ip)
//        {
//            if (StringHelper.IsNullOrWhiteSpace(ip))
//                ip = DEFAULT_IP;
//            EnsureValidIp(ip);
//            _address = ip;
//        }

//        private static Regex IpRegex = new Regex(@"^(?<First>2[0-4]\d|25[0-5]|[01]?\d\d?)\.(?<Second>2[0-4]\d|25[0-5]|[01]?\d\d?)\.(?<Third>2[0-4]\d|25[0-5]|[01]?\d\d?)\.(?<Fourth>2[0-4]\d|25[0-5]|[01]?\d\d?)$");
//        private static void EnsureValidIp(string ip)
//        {
//            if (!IpRegex.IsMatch(ip))
//                throw new ArgumentException(string.Format("Invalid IP address '{0}'.", ip));
//        }
//    }
//}
