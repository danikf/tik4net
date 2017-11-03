using InvertedTomato.TikLink.Commands;
using System;
using Xunit;

namespace InvertedTomato.TikLink {
    public class LoginTests {
        [Fact]
        public void Success() {
            using (var link = Link.Connect("52.64.228.166")) {
                Assert.True(Login.TryLogin(link, "agent", "test1234", out var message));
                Assert.Null(message);
            }


            // object oriented approach:
            using (var link = Link.Connect("52.64.228.166")) {
                if (!Login.TryLogin(link, "agent", "test1234", out var message)) {
                    // Uh-ho, broken, see "message" for details
                }
            }
        }

        [Fact]
        public void Failure() {
            using (var connection = Link.Connect("52.64.228.166")) {
                Assert.False(Login.TryLogin(connection, "agent", "test1234a", out var message));
                Assert.True(message != string.Empty);
            }
        }
    }
}
