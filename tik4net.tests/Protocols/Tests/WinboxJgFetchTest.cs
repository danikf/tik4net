// WinboxJgFetchTest.cs — fetch version-matched .jg catalog via WinBox M2 (port 8291),
// no HTTP. Discovery (2026-06-07): the on-disk file is "<name>.jg.gz" (gzip), NOT "<name>.jg".
// webfig serves it gzipped (Content-Encoding: gzip; 406 without Accept-Encoding: gzip),
// so the real file in /home/web/webfig/ is "roteros.jg.gz". mproxy [2,2] cmd=7 reads from
// that same dir → open "<name>.jg.gz", multi-chunk read, gunzip client-side.

using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Configuration;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;

namespace tik4net.tests
{
    [TestClass]
    public class WinboxJgFetchTest
    {
        private const int WINBOX_PORT = 8291;

        private static (string host, string user, string pass) Cfg() => (
            ConfigurationManager.AppSettings["host"],
            ConfigurationManager.AppSettings["user"],
            ConfigurationManager.AppSettings["pass"] ?? "");

        // Confirm: mproxy cmd=7 can open "<name>.jg.gz" (gzipped on-disk name) over port 8291,
        // and the gunzipped content is the JS-object-literal catalog.
        [TestMethod]
        public void Winbox_FetchJgGz_ViaMproxy_Works()
        {
            var (host, user, pass) = Cfg();
            using (var client = new WinboxM2Client())
            {
                client.Connect(host, WINBOX_PORT);
                client.Authenticate(host, WINBOX_PORT, user, pass);

                // Diagnostic: what does the plain "list" read return?
                string list = client.ReadListCatalog();
                Console.WriteLine($"list catalog: {list?.Length ?? -1} chars, head='{(list ?? "").Substring(0, Math.Min(60, (list ?? "").Length))}'");

                bool anyOk = false;
                foreach (var name in new[] { "roteros.jg.gz", "dhcp.jg.gz", "advtool.jg.gz" })
                {
                    byte[] raw;
                    try { raw = client.ReadFileBytes(name); }   // mproxy cmd=7 open + multi-chunk read
                    catch (Exception ex) { Console.WriteLine($"{name}: OPEN FAILED — {ex.Message}"); continue; }

                    if (raw == null || raw.Length == 0) { Console.WriteLine($"{name}: empty"); continue; }

                    bool isGzip = raw.Length > 2 && raw[0] == 0x1F && raw[1] == 0x8B;
                    Console.WriteLine($"{name}: {raw.Length}B, first8={BitConverter.ToString(raw.Take(8).ToArray())}, gzip-magic={isGzip}");
                    if (!isGzip) continue;

                    byte[] plain = Gunzip(raw);
                    string text = Encoding.UTF8.GetString(plain, 0, Math.Min(plain.Length, 64));
                    Console.WriteLine($"{name}: gunzipped {plain.Length}B, head='{text}'");
                    anyOk = anyOk || text.TrimStart().StartsWith("[{");

                    if (name == "roteros.jg.gz")
                    {
                        string dir = Path.GetFullPath(Environment.ExpandEnvironmentVariables(
                            ConfigurationManager.AppSettings["catalogDumpDir"] ?? @".\.tik4net"));
                        Directory.CreateDirectory(Path.Combine(dir, "via-mproxy"));
                        File.WriteAllBytes(Path.Combine(dir, "via-mproxy", "roteros.jg"), plain);
                        Console.WriteLine($"saved gunzipped roteros.jg ({plain.Length}B) to {dir}\\via-mproxy");
                    }
                }
                Assert.IsTrue(anyOk, "At least one <name>.jg.gz should fetch via mproxy and gunzip to a JS literal");
            }
        }

        private static byte[] Gunzip(byte[] gz)
        {
            using (var ms = new MemoryStream(gz))
            using (var gs = new GZipStream(ms, CompressionMode.Decompress))
            using (var outMs = new MemoryStream())
            {
                gs.CopyTo(outMs);
                return outMs.ToArray();
            }
        }
    }
}
