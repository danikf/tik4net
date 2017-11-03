using InvertedTomato.TikLink.Commands;
using System;
using Xunit;

namespace InvertedTomato.TikLink {
    public class LoginTests {
        [Fact]
        public void TryLogin_Success() {
            using (var link = Link.Connect("52.64.228.166")) {
                Assert.True(link.TryLogin( "agent", "test1234", out var message));
                Assert.Null(message);
            }
        }

        [Fact]
        public void TryLogin_Failure() {
            using (var link = Link.Connect("52.64.228.166")) {
                Assert.False(LoginCommand.TryLogin(link, "agent", "test1234a", out var message));
                Assert.True(message != string.Empty);
            }
        }


        [Fact]
        public void Login_Success() {
            using (var link = Link.Connect("52.64.228.166")) {
                link.Login("agent", "test1234");
            }
        }

        [Fact]
        public void Login_Failure() {
            using (var link = Link.Connect("52.64.228.166")) {
                Assert.Throws<AccessDeniedException>(() => {
                    link.Login("agent", "test1234a");
                });
            }
        }
    }
}
