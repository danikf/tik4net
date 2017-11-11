using System;
using System.IO;

namespace InvertedTomato.TikLink {
    public static class WordLength {
        public static void WriteLength(int length, Stream stream) { // TODO: rewrite more efficently
            if (length < 0) {
                throw new ArgumentOutOfRangeException(nameof(length), "Must be at least 0.");
            }
            if (null == stream) {
                throw new ArgumentNullException(nameof(stream));
            }

            if (length < 0x80) {
                var tmp = BitConverter.GetBytes(length);
                stream.Write(new byte[] { tmp[0] }, 0, 1);
                return;
            }
            if (length < 0x4000) {
                var tmp = BitConverter.GetBytes(length | 0x8000);
                stream.Write(new byte[] { tmp[1], tmp[0] }, 0, 2);
                return;
            }
            if (length < 0x200000) {
                var tmp = BitConverter.GetBytes(length | 0xC00000);
                stream.Write(new byte[] { tmp[2], tmp[1], tmp[0] }, 0, 3);
                return;
            }
            if (length < 0x10000000) {
                var tmp = BitConverter.GetBytes((uint)length | 0xE0000000);
                stream.Write(new byte[] { tmp[3], tmp[2], tmp[1], tmp[0] }, 0, 4);
                return;
            } else {
                var tmp = BitConverter.GetBytes(length);
                stream.Write(new byte[] { 0xF0, tmp[3], tmp[2], tmp[1], tmp[0] }, 0, 5);
                return;
            }
        }

        public static int ReadLength(Stream stream) { // TODO: rewrite more efficently
            if (null == stream) {
                throw new ArgumentNullException(nameof(stream));
            }

            int length = 0;
            byte readByte = (byte)stream.ReadByte();
            if ((readByte & 0x80) == 0x00) {
                length = readByte;
            } else if ((readByte & 0xC0) == 0x80) {
                length = ((readByte & 0x3F) << 8) + (byte)stream.ReadByte();
            } else if ((readByte & 0xE0) == 0xC0) {
                length = ((readByte & 0x1F) << 8) + (byte)stream.ReadByte();
                length = (length << 8) + (byte)stream.ReadByte();
            } else if ((readByte & 0xF0) == 0xE0) {
                length = ((readByte & 0xF) << 8) + (byte)stream.ReadByte();
                length = (length << 8) + (byte)stream.ReadByte();
                length = (length << 8) + (byte)stream.ReadByte();
            } else {
                length = (byte)stream.ReadByte();
                length = (length << 8) + (byte)stream.ReadByte();
                length = (length << 8) + (byte)stream.ReadByte();
                length = (length << 8) + (byte)stream.ReadByte();
            }

            return length;
        }
    }
}