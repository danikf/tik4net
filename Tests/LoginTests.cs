using InvertedTomato.TikLink;
using InvertedTomato.TikLink.Commands;
using System;
using Xunit;

namespace Tests {
    public class LoginTests {
        [Fact]
        public void TryLogin_Success() {
            using (var link = Link.Connect(Credentials.Current.Host)) {
                Assert.True(link.TryLogin(Credentials.Current.Username, Credentials.Current.Password, out var message));
                Assert.Null(message);
            }
        }

        [Fact]
        public void TryLogin_Failure() {
            using (var link = Link.Connect(Credentials.Current.Host)) {
                Assert.False(LoginCommand.TryLogin(link, Credentials.Current.Username, Credentials.Current.Password + "BAD", out var message));
                Assert.True(message != string.Empty);
            }
        }


        [Fact]
        public void Login_Success() {
            using (var link = Link.Connect(Credentials.Current.Host)) {
                link.Login(Credentials.Current.Username, Credentials.Current.Password);
            }
        }

        [Fact]
        public void Login_Failure() {
            using (var link = Link.Connect(Credentials.Current.Host)) {
                Assert.Throws<AccessDeniedException>(() => {
                    link.Login(Credentials.Current.Username, Credentials.Current.Password + "BAD");
                });
            }
        }
    }
}
