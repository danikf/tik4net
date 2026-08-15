// SshAuthProbeTest.cs — what does RouterOS actually do with a WRONG SSH password?
//
// P2.24 established that `admin` with an EMPTY password authenticates over SSH with method "none",
// so a wrong password is accepted. That says nothing about an account that HAS a password, which is
// the case that decides whether "SSH with a password" is an access control at all.
//
// Prints what happened rather than asserting a conclusion — it is a probe.
// [Ignore] keeps it out of the matrix; run via --filter.

using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Configuration;
using System.Linq;

namespace tik4net.integrationtests
{
    // Measured 2026-08-14 on 7.23.2 (see Docs/findings-cli.md §5):
    //   admin/'' (no password)      correct → OPENED, identity CHR, /user/print 2 rows
    //   admin/wrong (no password)   WRONG   → OPENED, identity CHR, /user/print 2 rows  ← no check at all
    //   test/test (has a password)  correct → OPENED, identity CHR, /user/print 2 rows
    //   test/wrong (has a password) WRONG   → REFUSED, "Permission denied (password)"
    [Ignore("SSH auth probe — deliberate wrong-password logins against a live router. Remove the attribute to run.")]
    [TestClass]
    public class SshAuthProbeTest
    {
        private static string Host => ConfigurationManager.AppSettings["host"];

        [TestMethod]
        public void Ssh_WrongPassword_WhatHappens()
        {
            // (user, password, what we expect to learn)
            var cases = new[]
            {
                new { User = "admin", Pass = "",                          Note = "empty-password account, correct (empty) password" },
                new { User = "admin", Pass = "definitely-wrong-password", Note = "empty-password account, WRONG password" },
                new { User = "test",  Pass = "test",                      Note = "password-protected account, correct password" },
                new { User = "test",  Pass = "definitely-wrong-password", Note = "password-protected account, WRONG password" },
            };

            foreach (var c in cases)
                Console.WriteLine($"{c.User}/'{c.Pass}' ({c.Note}): {TryLogin(c.User, c.Pass)}");
        }

        private static string TryLogin(string user, string pass)
        {
            try
            {
                using (var conn = ConnectionFactory.CreateConnection(TikConnectionType.Ssh))
                {
                    conn.Open(Host, user, pass);

                    // A shell is not the same as a usable session — run something and read it back.
                    string identity = conn.CreateCommand("/system/identity/print").ExecuteScalar();
                    int users = conn.CreateCommand("/user/print").ExecuteList().Count();

                    return $"OPENED — /system/identity/print => '{identity}', /user/print => {users} rows";
                }
            }
            catch (Exception ex)
            {
                return $"REFUSED — {ex.GetType().Name}: {ex.Message.Replace(Environment.NewLine, " ")}";
            }
        }
    }
}
