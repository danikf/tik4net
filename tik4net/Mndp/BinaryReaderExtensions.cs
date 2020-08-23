using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace tik4net.Mndp
{
    /// <summary>
    /// <see cref="BinaryReader"/> mikrotik specific extensions.
    /// </summary>
    public static class BinaryReaderExtensions
    {
        /// <summary>
        /// Reads word value from mikrotik binary format.
        /// </summary>
        public static UInt16 ReadWord(this BinaryReader reader)
        {
            byte[] b = reader.ReadBytes(2);
            Array.Reverse(b);

            return BitConverter.ToUInt16(b, 0);
        }
    }
}
