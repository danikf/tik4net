using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using tik4net.Connection;
using tik4net.Rest;

namespace tik4net.unittests.Rest
{
    /// <summary>
    /// What <see cref="RestConnection"/> sends and how it reads the answer, over a scripted local HTTP server: the
    /// response side (a failed or unreadable answer is never "no rows"), the request side (parameter names, row
    /// addressing, encoding) and the question of whether a trailing segment is a menu or a command.
    /// </summary>
    [TestClass]
    [DoNotParallelize]   // local listeners answer within a time budget
    public class RestConnectionWireTests
    {
        private const string Probe = "{\"version\":\"7.25\"}";

        private static ScriptedHttpServer.Reply Ok(string body) => new ScriptedHttpServer.Reply(200, body);

        private static ScriptedHttpServer Server(params ScriptedHttpServer.Reply[] afterProbe)
            => new ScriptedHttpServer(new[] { Ok(Probe) }.Concat(afterProbe).ToArray());

        private static RestConnection Open(ScriptedHttpServer server)
        {
            var conn = new RestConnection(useSsl: false)
            {
                ConnectTimeout = TimeSpan.FromSeconds(5),
                SendTimeout = TimeSpan.FromSeconds(5),
                ReceiveTimeout = TimeSpan.FromSeconds(5),
            };
            conn.Open("127.0.0.1", server.Port, "admin", "");
            return conn;
        }

        // The requests after the open probe.
        private static List<ScriptedHttpServer.Request> Sent(ScriptedHttpServer server) => server.Requests.Skip(1).ToList();

        private static ITikCommand Command(RestConnection conn, string path, params (string Name, string Value)[] parameters)
        {
            var cmd = conn.CreateCommand(path);
            foreach (var p in parameters)
                cmd.AddParameter(p.Name, p.Value, TikCommandParameterFormat.NameValue);
            return cmd;
        }

        // ── A failed request is an error, whatever its body ───────────────────

        [DataTestMethod]
        [DataRow(500, "")]
        [DataRow(503, "{}")]
        [DataRow(500, "null")]
        [DataRow(502, "[]")]
        public void AFailedRead_IsAnError_NotAnEmptyTable(int status, string body)
        {
            using (var server = Server(new ScriptedHttpServer.Reply(status, body)))
            using (var conn = Open(server))
            {
                var ex = Assert.ThrowsException<TikCommandTrapException>(() => conn.CreateCommand("/ip/address/print").ExecuteList());
                StringAssert.Contains(ex.Message, status.ToString());
            }
        }

        [TestMethod]
        public void AFailedAdd_ReportsTheFailure_NotAMissingId()
        {
            using (var server = Server(new ScriptedHttpServer.Reply(500, "")))
            using (var conn = Open(server))
            {
                var ex = Assert.ThrowsException<TikCommandTrapException>(
                    () => Command(conn, "/ip/address/add", ("address", "192.0.2.1/32")).ExecuteScalar());
                Assert.IsNotInstanceOfType(ex, typeof(TikAddIdNotReadException));
            }
        }

        [DataTestMethod]
        [DataRow("<html>busy</html>")]
        [DataRow("busy")]
        [DataRow("5")]
        [DataRow("[{\"a\":\"b\"")]
        [DataRow("[\"a\"]")]
        public void A200ThatIsNotRows_IsAnUnexpectedResponse(string body)
        {
            using (var server = Server(Ok(body)))
            using (var conn = Open(server))
            {
                var ex = Assert.ThrowsException<TikCommandUnexpectedResponseException>(
                    () => conn.CreateCommand("/ip/address/print").ExecuteList());
                StringAssert.Contains(ex.Message, body);
            }
        }

        [DataTestMethod]
        [DataRow("")]
        [DataRow("  ")]
        [DataRow("null")]
        [DataRow("[]")]
        [DataRow("{}")]
        public void AnEmptyAnswerStillReadsAsNoRows(string body)
        {
            using (var server = Server(Ok(body)))
            using (var conn = Open(server))
                Assert.AreEqual(0, conn.CreateCommand("/ip/address/print").ExecuteList().Count());
        }

        [TestMethod]
        public void AnArrayAndAnObjectBothReadAsRows()
        {
            using (var server = Server(Ok("[{\"name\":\"a\"},{\"name\":\"b\",\"x\":null}]"), Ok("{\"name\":\"c\"}")))
            using (var conn = Open(server))
            {
                var rows = conn.CreateCommand("/ip/address/print").ExecuteList().ToList();
                Assert.AreEqual(2, rows.Count);
                Assert.AreEqual("b", rows[1].GetResponseField("name"));
                Assert.AreEqual("", rows[1].GetResponseField("x"));
                Assert.AreEqual("c", conn.CreateCommand("/system/identity/print").ExecuteList().Single().GetResponseField("name"));
            }
        }

        [TestMethod]
        public void AnAddAnsweredWithAnUnreadableBody_IsAddIdNotRead()
        {
            // The row has very likely been created: the caller needs the exception that says so.
            using (var server = Server(Ok("<html>")))
            using (var conn = Open(server))
            {
                var ex = Assert.ThrowsException<TikAddIdNotReadException>(
                    () => Command(conn, "/ip/address/add", ("address", "192.0.2.1/32")).ExecuteScalar());
                StringAssert.Contains(ex.Message, "<html>");
            }
        }

        [TestMethod]
        public void AnHtml404OnACommand_IsNoSuchCommand_NotNoSuchItem()
        {
            using (var server = Server(new ScriptedHttpServer.Reply(404, "<html>404</html>", "text/html")))
            using (var conn = Open(server))
                Assert.ThrowsException<TikNoSuchCommandException>(() => conn.CreateCommand("/ip/address/print").ExecuteList());
        }

        [TestMethod]
        public void AJson404_IsNoSuchItem()
        {
            using (var server = Server(new ScriptedHttpServer.Reply(404, "{\"error\":404,\"message\":\"Not Found\"}")))
            using (var conn = Open(server))
                Assert.ThrowsException<TikNoSuchItemException>(
                    () => Command(conn, "/ip/address/set", (TikSpecialProperties.Id, "*1"), ("comment", "c")).ExecuteNonQuery());
        }

        // ── Parameter names and rows ──────────────────────────────────────────

        [TestMethod]
        public void APrefixedName_IsSentBare()
        {
            using (var server = Server(Ok("{}"), Ok("[]")))
            using (var conn = Open(server))
            {
                conn.CreateCommand("/ip/address/set", conn.CreateParameter("=.id", "*1"), conn.CreateParameter("=comment", "x")).ExecuteNonQuery();
                conn.CreateCommand("/ip/address/add", conn.CreateParameter("=address", "192.0.2.1/32")).ExecuteNonQuery();

                var sent = Sent(server);
                Assert.AreEqual("PATCH /rest/ip/address/*1 {\"comment\":\"x\"}", sent[0].ToString());
                Assert.AreEqual("PUT /rest/ip/address {\"address\":\"192.0.2.1/32\"}", sent[1].ToString());
            }
        }

        [TestMethod]
        public void GetByNumber_AddressesTheRow()
        {
            using (var server = Server(Ok("{\"mtu\":\"65536\"}")))
            using (var conn = Open(server))
            {
                string mtu = Command(conn, "/interface/get", ("number", "lo"), ("value-name", "mtu")).ExecuteScalar();

                Assert.AreEqual("65536", mtu);
                Assert.AreEqual("/rest/interface/lo?.proplist=mtu", Sent(server)[0].Path);
            }
        }

        [TestMethod]
        public void GetByName_InDotId_KeepsTheRowTheRouterAddressed()
        {
            // The router addresses a row by name and answers with its real .id, which is not what was asked for.
            using (var server = Server(Ok("{\".id\":\"*7\",\"name\":\"lo\"}")))
            using (var conn = Open(server))
            {
                string row = Command(conn, "/interface/get", (TikSpecialProperties.Id, "lo")).ExecuteScalar();
                StringAssert.Contains(row, "name=lo");
            }
        }

        [DataTestMethod]
        [DataRow("*1A", "*1A")]
        [DataRow("vlan/1 x?y", "vlan%2F1%20x%3Fy")]
        [DataRow("a#b", "a%23b")]
        public void TheIdIsEscapedInTheUrl_ExceptItsStar(string id, string inUrl)
        {
            using (var server = Server(Ok("{}"), Ok(""), Ok("{}")))
            using (var conn = Open(server))
            {
                Command(conn, "/ip/address/set", (TikSpecialProperties.Id, id), ("comment", "c")).ExecuteNonQuery();
                Command(conn, "/ip/address/remove", (TikSpecialProperties.Id, id)).ExecuteNonQuery();
                Command(conn, "/ip/address/get", (TikSpecialProperties.Id, id)).ExecuteList();

                var sent = Sent(server);
                Assert.AreEqual("/rest/ip/address/" + inUrl, sent[0].Path);
                Assert.AreEqual("/rest/ip/address/" + inUrl, sent[1].Path);
                Assert.AreEqual("/rest/ip/address/" + inUrl, sent[2].Path);
            }
        }

        [TestMethod]
        public void AValueNeedingJsonEscapes_StaysValidJson()
        {
            using (var server = Server(Ok("[]")))
            using (var conn = Open(server))
            {
                Command(conn, "/ip/firewall/filter/unset", (TikSpecialProperties.Id, "*1"), ("value-name", "a\"b")).ExecuteNonQuery();

                using (var doc = JsonDocument.Parse(Sent(server)[0].Body))
                    Assert.AreEqual("a\"b", doc.RootElement.GetProperty("value-name").GetString());
            }
        }

        [TestMethod]
        public void RemoveAndUnsetByNumbers_AreForwarded()
        {
            // Measured on 7.25: both forms are the router's own, and it answers 200.
            using (var server = Server(Ok("[]"), Ok("[]")))
            using (var conn = Open(server))
            {
                Command(conn, "/ip/address/remove", ("numbers", "*1")).ExecuteNonQuery();
                Command(conn, "/ip/firewall/raw/unset", ("numbers", "*2"), ("value-name", "src-address")).ExecuteNonQuery();

                var sent = Sent(server);
                Assert.AreEqual("POST /rest/ip/address/remove {\"numbers\":\"*1\"}", sent[0].ToString());
                Assert.AreEqual("POST /rest/ip/firewall/raw/unset {\"numbers\":\"*2\",\"value-name\":\"src-address\"}", sent[1].ToString());
            }
        }

        [TestMethod]
        public void AnUnsetTheRouterRefuses_IsTheRoutersRefusal()
        {
            // No client-side guard: on a singleton the router answers 'no such command', as it does on the API.
            using (var server = Server(new ScriptedHttpServer.Reply(400, "{\"detail\":\"no such command\",\"error\":400,\"message\":\"Bad Request\"}")))
            using (var conn = Open(server))
            {
                Assert.ThrowsException<TikNoSuchCommandException>(
                    () => Command(conn, "/ip/dns/unset", (TikSpecialProperties.Id, null), ("value-name", "cache-size")).ExecuteNonQuery());

                Assert.AreEqual("POST /rest/ip/dns/unset {\"value-name\":\"cache-size\"}", Sent(server)[0].ToString());
            }
        }

        [TestMethod]
        public void SetOnAListOfIds_IsPostedToSet()
        {
            // PATCH with the list in the URL is applied and then answered with 404 (7.25): a false failure on a write.
            using (var server = Server(Ok("[]"), Ok("")))
            using (var conn = Open(server))
            {
                Command(conn, "/ip/address/set", (TikSpecialProperties.Id, "*1,*2"), ("comment", "c")).ExecuteNonQuery();
                Command(conn, "/ip/address/remove", (TikSpecialProperties.Id, "*1,*2")).ExecuteNonQuery();

                var sent = Sent(server);
                Assert.AreEqual("POST /rest/ip/address/set {\".id\":\"*1,*2\",\"comment\":\"c\"}", sent[0].ToString());
                Assert.AreEqual("DELETE /rest/ip/address/*1%2C*2 ", sent[1].ToString());
            }
        }

        [TestMethod]
        public void ControlCharactersAndQuotesAreEscaped_AndNothingElse()
        {
            using (var server = Server(Ok("[]")))
            using (var conn = Open(server))
            {
                string value = "a\"b\\c\r\n\t\u0001/\u0159";
                Command(conn, "/ip/address/set", (TikSpecialProperties.Id, "*1"), ("comment", value)).ExecuteNonQuery();

                using (var doc = JsonDocument.Parse(Sent(server)[0].Body))
                    Assert.AreEqual(value, doc.RootElement.GetProperty("comment").GetString());
            }
        }

        // ── Encoding ──────────────────────────────────────────────────────────

        [TestMethod]
        public void TextGoesOutInTheConnectionsEncoding_NotAsJsonEscapes()
        {
            // RouterOS stores a \uXXXX escape as one code-page byte and a raw UTF-8 sequence as it arrives (7.25), so
            // an escape is read back as something else on every transport.
            using (var server = Server(Ok("[]")))
            using (var conn = Open(server))
            {
                Command(conn, "/ip/address/set", (TikSpecialProperties.Id, "*1"), ("comment", "ř + < > ' & \U0001F600")).ExecuteNonQuery();

                var sent = Sent(server)[0];
                Assert.AreEqual("{\"comment\":\"ř + < > ' & \U0001F600\"}", sent.Body);
                Assert.AreEqual("application/json", sent.ContentType, "RouterOS may refuse a charset (findings §1)");
            }
        }

        [TestMethod]
        public void TheConnectionsEncoding_AppliesToBothDirections()
        {
            var cp = Encoding.GetEncoding("iso-8859-1");
            using (var server = Server(new ScriptedHttpServer.Reply(200, cp.GetBytes("[{\"comment\":\"é\"}]")), Ok("[]")))
            using (var conn = Open(server))
            {
                conn.Encoding = cp;
                Assert.AreEqual("é", conn.CreateCommand("/ip/address/print").ExecuteList().Single().GetResponseField("comment"));
                Command(conn, "/ip/address/set", (TikSpecialProperties.Id, "*1"), ("comment", "é")).ExecuteNonQuery();

                CollectionAssert.AreEqual(cp.GetBytes("{\"comment\":\"é\"}"), Sent(server)[1].BodyBytes);
            }
        }

        // ── The trace ─────────────────────────────────────────────────────────

        [TestMethod]
        public void TheWireTraceCarriesTheRequestBody()
        {
            using (var server = Server(Ok("[]")))
            using (var conn = Open(server))
            {
                var written = new List<string>();
                conn.OnWriteRow += (sender, e) => written.Add(e.Word);
                conn.CreateCommand("/ip/address/print", conn.CreateParameter("?address", "192.0.2.1/32")).ExecuteList();

                Assert.AreEqual("POST /ip/address/print {\".query\":[\"address=192.0.2.1/32\"]}", written.Single());
            }
        }

        // ── Menu or command: the router is asked ──────────────────────────────

        private const string InspectCmd = "[{\"name\":\"x\",\"node-type\":\"cmd\",\"type\":\"self\"},{\"name\":\"a\",\"node-type\":\"arg\",\"type\":\"child\"}]";
        private const string InspectDir = "[{\"name\":\"x\",\"node-type\":\"dir\",\"type\":\"self\"},{\"name\":\"print\",\"node-type\":\"cmd\",\"type\":\"child\"}]";
        private const string InspectPath = "[{\"name\":\"x\",\"node-type\":\"path\",\"type\":\"self\"}]";

        [TestMethod]
        public void ACommandTheRouterNames_IsPostedToItsOwnPath()
        {
            using (var server = Server(Ok(InspectCmd), Ok("[{\"ret\":\"ok\"}]")))
            using (var conn = Open(server))
            {
                conn.CreateCommand("/ip/dhcp-client/renew").ExecuteList();

                var sent = Sent(server);
                Assert.AreEqual("POST /rest/console/inspect {\"request\":\"child\",\"path\":\"ip,dhcp-client,renew\"}", sent[0].ToString());
                Assert.AreEqual("POST /rest/ip/dhcp-client/renew ", sent[1].ToString());
            }
        }

        [DataTestMethod]
        [DataRow(InspectDir)]
        [DataRow(InspectPath)]
        public void AMenuTheRouterNames_IsAnImplicitPrint(string inspect)
        {
            using (var server = Server(Ok(inspect), Ok("[]")))
            using (var conn = Open(server))
            {
                // 'profile' is also a monitor verb (/tool/profile): the router tells the two apart.
                conn.CreateCommand("/ppp/profile").ExecuteList();

                Assert.AreEqual("GET /rest/ppp/profile ", Sent(server)[1].ToString());
            }
        }

        [TestMethod]
        public void ThePathIsAskedOncePerOpen()
        {
            using (var server = Server(Ok(InspectDir), Ok("[]"), Ok("[]")))
            using (var conn = Open(server))
            {
                conn.CreateCommand("/ppp/profile").ExecuteList();
                conn.CreateCommand("/ppp/profile").ExecuteList();

                Assert.AreEqual(1, Sent(server).Count(r => r.Path == "/rest/console/inspect"));
            }
        }

        [TestMethod]
        public void APathTheRouterDoesNotList_IsPostedAsGiven()
        {
            // /ping answers on REST and inspect lists nothing for it (7.25); a path that does not exist gets the same
            // 400 from POST as from GET.
            using (var server = Server(Ok("[]"), Ok("[{\"seq\":\"0\"}]")))
            using (var conn = Open(server))
            {
                Command(conn, "/ping", ("address", "127.0.0.1"), ("count", "1")).ExecuteList();

                StringAssert.StartsWith(Sent(server)[1].ToString(), "POST /rest/ping {");
            }
        }

        [TestMethod]
        public void WhenTheRouterRefusesTheQuestion_AMonitorVerbIsACommand()
        {
            var notAllowed = new ScriptedHttpServer.Reply(500, "{\"detail\":\"std failure: not allowed (9)\",\"error\":500,\"message\":\"Internal Server Error\"}");
            using (var server = Server(notAllowed, Ok("[]")))
            using (var conn = Open(server))
            {
                Command(conn, "/ping", ("address", "127.0.0.1")).ExecuteList();

                StringAssert.StartsWith(Sent(server)[1].ToString(), "POST /rest/ping");
            }
        }

        [TestMethod]
        public void WhenTheRouterRefusesTheQuestion_AnythingElseIsAMenu()
        {
            var notAllowed = new ScriptedHttpServer.Reply(500, "{\"detail\":\"std failure: not allowed (9)\",\"error\":500,\"message\":\"Internal Server Error\"}");
            using (var server = Server(notAllowed, Ok("[]")))
            using (var conn = Open(server))
            {
                conn.CreateCommand("/ip/address").ExecuteList();

                Assert.AreEqual("GET /rest/ip/address ", Sent(server)[1].ToString());
            }
        }

        [DataTestMethod]
        [DataRow("/ip/address/print")]
        [DataRow("/ip/address/get")]
        public void AVerbWithItsOwnShape_IsNotAsked(string path)
        {
            using (var server = Server(Ok("[]")))
            using (var conn = Open(server))
            {
                conn.CreateCommand(path).ExecuteList();
                Assert.AreEqual(1, Sent(server).Count);
                Assert.AreEqual("GET", Sent(server)[0].Method);
            }
        }

        [TestMethod]
        public void ANonQueryIsNotAsked()
        {
            using (var server = Server(Ok("[]")))
            using (var conn = Open(server))
            {
                Command(conn, "/log/error", ("message", "m")).ExecuteNonQuery();
                Assert.AreEqual("POST /rest/log/error {\"message\":\"m\"}", Sent(server).Single().ToString());
            }
        }

        [TestMethod]
        public void DescribingAMenuWorks_InspectItselfIsNotAsked()
        {
            using (var server = Server(Ok(InspectDir)))
            using (var conn = Open(server))
            {
                var schema = conn.DescribeMenu("/ip/address");

                Assert.AreEqual("print", schema.Commands.Single());
                Assert.AreEqual("/rest/console/inspect", Sent(server).Single().Path);
            }
        }

        // ── Open ──────────────────────────────────────────────────────────────

        [DataTestMethod]
        [DataRow(404, "<html>404 Not Found</html>", "has no REST API")]
        [DataRow(200, "<html>a router's or anybody's web page</html>", "not JSON")]
        [DataRow(503, "", "not JSON")]
        [DataRow(403, "Forbidden", "not JSON")]
        public void AnAnswerThatIsNotTheRestApi_RefusesTheOpen(int status, string body, string message)
        {
            using (var server = new ScriptedHttpServer(new ScriptedHttpServer.Reply(status, body, "text/html")))
            using (var conn = new RestConnection(useSsl: false))
            {
                var ex = Assert.ThrowsException<TikConnectionProtocolMismatchException>(
                    () => conn.Open("127.0.0.1", server.Port, "admin", ""));

                StringAssert.Contains(ex.Message, message);
                Assert.AreEqual(server.Port, ex.Port);
                Assert.IsFalse(ex.TlsAttempted);
                Assert.IsFalse(conn.IsOpened);
            }
        }

        [TestMethod]
        public void AUserWhoMayNotReadTheProbesMenu_StillOpens()
        {
            // Measured on 7.25 for a group with rest-api and without read: 500 with this body. The login was accepted.
            using (var server = new ScriptedHttpServer(new ScriptedHttpServer.Reply(500,
                "{\"detail\":\"std failure: not allowed (9)\",\"error\":500,\"message\":\"Internal Server Error\"}")))
            using (var conn = new RestConnection(useSsl: false))
            {
                conn.Open("127.0.0.1", server.Port, "admin", "");
                Assert.IsTrue(conn.IsOpened);
            }
        }

        [TestMethod]
        public void A401_IsALoginFailure()
        {
            // The router's answer for a wrong password, and for a user whose group lacks rest-api (7.25).
            using (var server = new ScriptedHttpServer(new ScriptedHttpServer.Reply(401, "{\"error\":401,\"message\":\"Unauthorized\"}")))
            using (var conn = new RestConnection(useSsl: false))
            {
                Assert.ThrowsException<TikConnectionLoginException>(() => conn.Open("127.0.0.1", server.Port, "admin", ""));
                Assert.IsFalse(conn.IsOpened);
            }
        }
    }
}
