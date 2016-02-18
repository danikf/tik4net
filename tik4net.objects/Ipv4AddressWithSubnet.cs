//using System;
//using System.Collections.Generic;
//using System.Linq;
//using System.Text;
//using System.Text.RegularExpressions;

//namespace tik4net.Objects
//{
//    /// <summary>
//    /// IP address in linux format: 192.168.1.1/24 or 192.168.1.1 (=192.168.1.1/32)
//    /// </summary>
//    public class Ipv4AddressWithSubnet
//    {
//        private const string DEFAULT_IP = "0.0.0.0";
//        private string _address;

//        /// <summary>
//        /// IP address as string in format 192.168.1.1/24 or 192.168.1.1
//        /// </summary>
//        public static implicit operator string (Ipv4AddressWithSubnet ip) { return ip == null ? DEFAULT_IP : ip.Address; }

//        /// <summary>
//        /// IP address as string in format 192.168.1.1/24 or 192.168.1.1
//        /// </summary>
//        public static implicit operator Ipv4AddressWithSubnet(string ip)
//        {
//            return new Ipv4AddressWithSubnet(ip);
//        }

//        /// <summary>
//        /// Ip address in format 192.168.1.1/24 or 192.168.1.1
//        /// </summary>
//        public string Address
//        {
//            get { return _address; }
//            set
//            {
//                EnsureValidIpWithMask(value);
//                _address = RemoveTailing32Subnet(value);
//            }
//        }

//        /// <summary>
//        /// IpAddress - 0.0.0.0/32
//        /// </summary>
//        public Ipv4AddressWithSubnet()
//        {
//            _address = DEFAULT_IP;
//        }

//        /// <summary>
//        /// Ctor
//        /// </summary>
//        /// <param name="ipWithMask">IP address in format 192.168.1.1/24 or 192.168.1.1</param>
//        public Ipv4AddressWithSubnet(string ipWithMask)
//        {
//            if (StringHelper.IsNullOrWhiteSpace(ipWithMask))
//                ipWithMask = DEFAULT_IP;
//            EnsureValidIpWithMask(ipWithMask);
//            _address = RemoveTailing32Subnet(ipWithMask);
//        }

//        private static Regex IpWithMaskRegex = new Regex(@"^(?<First>2[0-4]\d|25[0-5]|[01]?\d\d?)\.(?<Second>2[0-4]\d|25[0-5]|[01]?\d\d?)\.(?<Third>2[0-4]\d|25[0-5]|[01]?\d\d?)\.(?<Fourth>2[0-4]\d|25[0-5]|[01]?\d\d?)(/(?<Mask>([0-9]|[1-2][0-9]|30|31|32)))?$");
//        private static void EnsureValidIpWithMask(string ip)
//        {
//            if (!IpWithMaskRegex.IsMatch(ip))
//                throw new ArgumentException(string.Format("Invalid IP address '{0}'.", ip));
//        }

//        private static string RemoveTailing32Subnet(string ipWithSubnet)
//        {
//            if (ipWithSubnet.EndsWith("/32"))
//                return ipWithSubnet.Substring(0, ipWithSubnet.Length - 3);
//            else
//                return ipWithSubnet;
//        }
//    }
//}
