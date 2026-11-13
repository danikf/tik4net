// McpPasswordReferenceTests.cs — the MCP server reads a password reference only when its name starts with TIK4NET_, and
// never dpapi:, because the connection string is written by a model that also chooses the host. net8.0 only.

#nullable enable

using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using tik4net.mcp;

namespace tik4net.unittests.Mcp
{
    [TestClass]
    public class McpPasswordReferenceTests
    {
        [DataTestMethod]
        [DataRow("secret")]
        [DataRow("plain:env:GITHUB_TOKEN")]
        [DataRow("env:TIK4NET_LAB_PW")]
        [DataRow("ENV:tik4net_lab_pw")]
        [DataRow("file:C:/any/where/TIK4NET_lab.txt")]
        [DataRow("file:/run/secrets/TIK4NET_lab")]
        [DataRow("file:TIK4NET_lab.pw")]
        public void Allowed(string value)
            => MikroTikTools.CheckPasswordReference(value, "password");

        [DataTestMethod]
        [DataRow("env:GITHUB_TOKEN")]
        [DataRow("env:AWS_SECRET_ACCESS_KEY")]
        [DataRow("file:C:/Users/x/.ssh/id_rsa")]
        [DataRow("file:/home/TIK4NET_x/.ssh/id_rsa")]
        [DataRow("file:/etc/TIK4NET_x/../passwd")]
        [DataRow("dpapi:AQAAANCMnd8BFdERjHoAwE/Cl+sBAAAA")]
        public void Refused(string value)
        {
            var ex = Assert.ThrowsException<ArgumentException>(() => MikroTikTools.CheckPasswordReference(value, "romon.password"));
            StringAssert.Contains(ex.Message, "romon.password");
        }

        [TestMethod]
        public void EveryPasswordKeyOfAConnectionString_IsChecked()
        {
            MikroTikTools.CheckPasswordReferences("host=r;user=u;password=env:TIK4NET_X;romon.password=plain:x");
            Assert.ThrowsException<ArgumentException>(() => MikroTikTools.CheckPasswordReferences("host=r;user=u;password=env:PATH"));
            Assert.ThrowsException<ArgumentException>(() => MikroTikTools.CheckPasswordReferences("host=r;user=u;PWD=env:PATH"));
            Assert.ThrowsException<ArgumentException>(() => MikroTikTools.CheckPasswordReferences(
                "host=AA:BB:CC:DD:EE:01;user=u;romon.host=192.0.2.9;romon.user=a;Romon.Password=file:/etc/passwd"));
        }
    }
}
