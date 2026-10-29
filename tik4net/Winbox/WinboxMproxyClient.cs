using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using tik4net.Diagnostics;

namespace tik4net.Winbox
{
    /// <summary>
    /// The file transfer the catalog loads through: the mproxy handler <c>[2,2]</c>'s open and chunked read of a
    /// router file (the plugin <c>list</c>, a <c>&lt;name&gt;.jg.gz</c>), over the native connection's M2 operations.
    /// </summary>
    internal static class WinboxMproxyClient
    {
        // ── mproxy .jg fetch (gzip <name>.jg.gz over [2,2] cmd=7) ──────────────

        // `uniqueName` must be the version-stamped name from the mproxy "list" catalog
        // ("roteros-33a7039ff432.jg"), never the stable plugin name. The stable name is a trap: mproxy
        // *opens* "roteros.jg.gz" and even reports the right size, but the subsequent read never answers
        // and takes the whole M2 channel down with it, so every later plugin fails too.
        internal static string? FetchJg(WinboxNativeM2Operations ops, string uniqueName)
        {
            // The on-disk file in /home/web/webfig/ is "<unique>.gz" (gzip), served by the mproxy static
            // handler via cmd=7 (NOT cmd=3 = /var/pckg, which CHR denies). A refused open is harmless —
            // it is a clean error reply and the channel survives it.
            int handle = Open(ops, uniqueName + ".gz", WinboxM2Protocol.Mproxy.OpenStatic);
            if (handle < 0) handle = Open(ops, uniqueName, WinboxM2Protocol.Mproxy.OpenStatic);
            if (handle < 0) return null;

            byte[]? raw = Read(ops, handle);
            if (raw == null || raw.Length == 0) return null;

            // .jg.gz is gzip; a plain .jg is already text. Detect the gzip magic.
            if (raw.Length > 2 && raw[0] == 0x1F && raw[1] == 0x8B)
                raw = GzipDecompress(raw);
            return Encoding.UTF8.GetString(raw);
        }

        internal static int Open(WinboxNativeM2Operations ops, string filename, int openCmd)
        {
            byte[] msg = M2Message.BuildM2(
                M2Message.SysToArr(WinboxM2Protocol.Mproxy.Handler), M2Message.SysFrom(),
                M2Message.BoolSys(WinboxM2Protocol.SysKey.ReplyExpected, true), ops.NextReqIdField(),
                M2Message.U8Sys(WinboxM2Protocol.SysKey.Command, (byte)openCmd),  // OpenStatic(7) / OpenVarPkg(3)
                M2Message.StringUser(WinboxM2Protocol.Mproxy.Key.FileName, filename));
            byte[] resp = ops.SendReceive(msg);
            try { return M2Message.ParseSessionId(resp); }
            catch { return -1; }
        }

        private const int MproxyChunk = WinboxM2Protocol.Mproxy.ChunkSize;

        internal static byte[]? Read(WinboxNativeM2Operations ops, int handle)
        {
            var all = new List<byte>();
            for (int guard = 0; guard < 256; guard++)
            {
                byte[] msg = M2Message.BuildM2(
                    M2Message.SysToArr(WinboxM2Protocol.Mproxy.Handler), M2Message.SysFrom(),
                    M2Message.BoolSys(WinboxM2Protocol.SysKey.ReplyExpected, true), ops.NextReqIdField(),
                    M2Message.SessionIdField(handle),
                    M2Message.U32User(WinboxM2Protocol.Mproxy.Key.MaxChunk, MproxyChunk),
                    M2Message.U8Sys(WinboxM2Protocol.SysKey.Command, WinboxM2Protocol.Mproxy.Read));  // read chunk
                byte[]? resp = ops.SendReceive(msg);
                byte[]? chunk = ExtractChunk(resp);
                if (chunk == null || chunk.Length == 0) break;
                all.AddRange(chunk);
                if (chunk.Length < MproxyChunk) break;
            }
            return all.Count > 0 ? all.ToArray() : null;
        }

        // Pull the raw/string file-content field (user namespace) out of a read response: the first non-empty one the
        // one TLV walk (M2Message.Walk) finds whole. An empty or cut-off field is stepped over by the walk's own rule —
        // a hand-written walk here once skipped an empty field from past its length and read the next field's first
        // byte as a length.
        internal static byte[]? ExtractChunk(byte[]? resp)
        {
            if (resp == null || resp.Length < 2 || resp[0] != 'M' || resp[1] != '2') return resp;
            foreach (var f in M2Message.Walk(resp))
            {
                int ftype = M2Message.FType(f.Type);
                if ((f.Key >> 16) != 0 || (ftype != M2Message.FType(M2Message.TypeByte.String)
                                           && ftype != M2Message.FType(M2Message.TypeByte.Raw)))
                    continue;
                // The value follows its length: one byte for the short form, four for the long, two otherwise.
                int lenWidth = (f.Type & M2Message.TypeByte.Short) != 0 ? 1 : (f.Type & M2Message.TypeByte.Long) != 0 ? 4 : 2;
                int start = f.ValueStart + lenWidth;
                if (f.End > resp.Length || f.End <= start) continue;   // cut off, or empty
                return resp.Skip(start).Take(f.End - start).ToArray();
            }
            return null;
        }

        private static byte[] GzipDecompress(byte[] compressed)
        {
            using (var ms = new MemoryStream(compressed))
            using (var gz = new GZipStream(ms, CompressionMode.Decompress))
            using (var outMs = new MemoryStream())
            {
                gz.CopyTo(outMs);
                return outMs.ToArray();
            }
        }
    }
}
