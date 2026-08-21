using System.Configuration;
using System.Linq;
using tik4net.Objects;
using tik4net.Objects.Interface;

namespace tik4net.examples
{
    /// <summary>
    /// One task — find an interface by its comment, keep its <c>.id</c>, then write a new comment back
    /// addressing the row by that <c>.id</c> — written three times, once per API level.
    ///
    /// This is the code behind the wiki page
    /// <see href="https://github.com/danikf/tik4net/wiki/One-task-on-every-transport-and-API-level">
    /// One task on every transport and API level</see>; the file exists so the snippets on that page are
    /// compiled rather than only proof-read.
    ///
    /// None of the three methods names a transport. They take an open <see cref="ITikConnection"/> and run
    /// unchanged on every one of them — which is the point the page makes, and <see cref="OpenAny"/> is the
    /// only part that differs.
    /// </summary>
    static class OneTaskEveryTransportExamples
    {
        private const string Marker = "managed-by-tik4net";
        private const string NewComment = "managed-by-tik4net (checked)";

        /// <summary>
        /// Level 1 — the low-level API: raw request words in, raw sentences out.
        /// </summary>
        public static void LowLevel(ITikConnection connection)
        {
            // '?' words are filters. The router answers one !re sentence per matching row, then !done.
            var findResponse = connection.CallCommandSync("/interface/print", "?comment=" + Marker);

            // Only !re sentences carry rows; the trailing !done is not one, so filter by type rather than
            // taking the first sentence.
            var row = findResponse.OfType<ITikReSentence>().SingleOrDefault();
            if (row == null)
                return;                       // no interface carries that comment

            string id = row.GetId();          // the .id word, in the router's '*2' form

            // '=' words are name-value. Addressing the row by .id is what makes this an update rather than
            // a second search.
            var setResponse = connection.CallCommandSync("/interface/set",
                "=.id=" + id,
                "=comment=" + NewComment);

            // On the binary API a rejected command comes back as a sentence, not as an exception — so the
            // result has to be inspected. On every other transport this same call throws instead (there is
            // no !trap on the wire to hand back), which is why the ADO.NET-like level below is the portable
            // way to write error handling.
            var trap = setResponse.OfType<ITikTrapSentence>().FirstOrDefault();
            if (trap != null)
                System.Console.WriteLine("update refused: " + trap.Message);
        }

        /// <summary>
        /// Level 2 — the ADO.NET-like API: commands, parameters and typed Execute* calls.
        /// </summary>
        public static void AdoNetLike(ITikConnection connection)
        {
            // The parameter FORMAT is decided by the Execute* call, not stated here: ExecuteSingleRowOrDefault
            // reads, so 'comment' goes out as the filter ?comment=… .
            var findCmd = connection.CreateCommandAndParameters("/interface/print", "comment", Marker);
            var row = findCmd.ExecuteSingleRowOrDefault();   // null when nothing matches
            if (row == null)
                return;

            string id = row.GetId();

            // ExecuteNonQuery writes, so the same helper now emits =comment=… and =.id=… .
            var updateCmd = connection.CreateCommandAndParameters("/interface/set",
                "comment", NewComment,
                TikSpecialProperties.Id, id);
            updateCmd.ExecuteNonQuery();     // throws TikCommandTrapException when the router refuses
        }

        /// <summary>
        /// Level 3 — the high-level O/R mapper: typed entities, and the whole object stands in for the id.
        /// </summary>
        public static void OrMapper(ITikConnection connection)
        {
            var iface = connection.LoadSingleOrDefault<Interface>(
                connection.CreateParameter("comment", Marker));
            if (iface == null)
                return;

            // No .id is handled by hand: the entity carries it (iface.Id), and Save() addresses the row with
            // it. Change tracking means the /set that goes out carries =comment= and nothing else.
            iface.Comment = NewComment;
            connection.Save(iface);          // throws TikCommandTrapException when the router refuses
        }

        /// <summary>
        /// The only transport-dependent code in this file. Every option is applied through
        /// <see cref="TikConnectionSetup"/>, so the same object opens any transport.
        /// </summary>
        public static ITikConnection OpenAny(TikConnectionType connectionType)
        {
            var setup = new TikConnectionSetup(
                ConfigurationManager.AppSettings["host"],
                ConfigurationManager.AppSettings["user"],
                ConfigurationManager.AppSettings["pass"])
            {
                // Only read by the MAC-layer transports (MacTelnet, WinboxCliMac, WinboxNativeMac); when it
                // is left null they discover the router by MNDP instead.
                RouterMac = ConfigurationManager.AppSettings["routerMac"],

                // Only read by the TLS transports (ApiSsl, RestSsl). False since 4.0 — set it for a router
                // with a self-signed certificate, as lab routers usually have.
                AllowInvalidCertificate = true,
            };

            // TikConnectionType.Ssh additionally needs tik4net.Ssh.Tik4NetSsh.Register() once at startup —
            // it lives in the separate tik4net.ssh package, which this project does not reference.
            return setup.Create(connectionType);
        }

        /// <summary>Runs all three levels over one transport.</summary>
        public static void RunAll(TikConnectionType connectionType)
        {
            using (var connection = OpenAny(connectionType))
            {
                LowLevel(connection);
                AdoNetLike(connection);
                OrMapper(connection);
            }
        }
    }
}
