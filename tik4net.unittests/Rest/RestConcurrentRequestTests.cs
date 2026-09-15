using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using tik4net.Connection;
using tik4net.Rest;

namespace tik4net.unittests.Rest
{
    /// <summary>
    /// Concurrent commands on one <see cref="RestConnection"/> each get their own reply, against a peer that — like
    /// RouterOS — answers only the first of several requests queued on one HTTP connection.
    /// </summary>
    /// <remarks>
    /// The case this pins is the .NET Framework leg. There <c>HttpClientHandler</c> is built on
    /// <c>HttpWebRequest</c>, which allows two connections per host and pipelines every further request onto a
    /// connection still waiting for its answer. RouterOS never answers those, so a third concurrent command
    /// timed out, and a reply could be handed to the wrong caller. On net8.0 <c>SocketsHttpHandler</c> never
    /// pipelines, so the same test there checks only that the concurrency cap does not deadlock.
    /// </remarks>
    [TestClass]
    public class RestConcurrentRequestTests
    {
        private const int Commands = 12;

        [TestMethod]
        public async Task ConcurrentCommandsAreNeverQueuedOnABusyHttpConnection()
        {
            string[] menus = { "/interface", "/ip/address", "/ip/route", "/system/identity" };

            using (var server = new KeepAliveHttpServer(responseDelayMs: 200))
            using (var conn = new RestConnection(useSsl: false))
            {
#if NETFRAMEWORK
                // .NET Framework exempts loopback from the per-host connection limit, so against 127.0.0.1 nothing
                // would ever queue. A router on the network gets the default of 2; this gives the fake one the same.
                System.Net.ServicePointManager.FindServicePoint(new Uri($"http://127.0.0.1:{server.Port}/rest"))
                    .ConnectionLimit = 2;
#endif
                conn.SendTimeout = 5000;     // a request is bounded by the larger of the two
                conn.ReceiveTimeout = 5000;
                conn.Open("127.0.0.1", server.Port, "admin", "");

                var tasks = Enumerable.Range(0, Commands).Select(async i =>
                {
                    string menu = menus[i % menus.Length];
                    var descriptor = new TikCommandDescriptor(menu + "/print", new List<ITikCommandParameter>());
                    try
                    {
                        var rows = await conn.InvokeRunPrintAsync(descriptor, CancellationToken.None).ConfigureAwait(false);
                        string name = rows.Single().Words["name"];
                        return name.EndsWith(menu, StringComparison.Ordinal) ? null
                            : $"command {i} ({menu}) got the reply to '{name}'";
                    }
                    catch (Exception ex)
                    {
                        return $"command {i} ({menu}): {ex.GetType().Name}: {ex.Message}";
                    }
                }).ToArray();

                string[] problems = (await Task.WhenAll(tasks).ConfigureAwait(false)).Where(p => p != null).ToArray();

                Assert.AreEqual(0, problems.Length,
                    $"{server.RequestsDiscarded} request(s) were queued behind another on one HTTP connection:"
                    + Environment.NewLine + string.Join(Environment.NewLine, problems));
                Assert.AreEqual(0, server.RequestsDiscarded, "a request was pipelined even though every command succeeded");
            }
        }
    }
}
