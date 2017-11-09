using InvertedTomato.TikLink;
using System.IO;
using System.Net.Sockets;
using Xunit;

namespace Tests {
    public class ConnectTests {
        [Fact]
        public void Connect_Success() {
            var link = Link.Connect(Credentials.Current.Host, Credentials.Current.Username, Credentials.Current.Password);
            Assert.False(link.IsDisposed);
            link.Dispose();
            Assert.True(link.IsDisposed);
        }

        [Fact]
        public void Connect_BadHost() {
            Assert.Throws<SocketException>(() => {
                Link.Connect(Credentials.Current.Host + "BAD", Credentials.Current.Username, Credentials.Current.Password);
            });
        }

        [Fact]
        public void Connect_BadUsername() {
            Assert.Throws<IOException>(() => {
                Link.Connect(Credentials.Current.Host, Credentials.Current.Username + "BAD", Credentials.Current.Password);
            });
        }

        [Fact]
        public void Connect_BadPassword() {
            Assert.Throws<IOException>(() => {
                Link.Connect(Credentials.Current.Host, Credentials.Current.Username, Credentials.Current.Password + "BAD");
            });
        }

        [Fact]
        public void ConnectSecure_Success() {
            Link.EnableSecure(Credentials.Current.Host, Credentials.Current.Username, Credentials.Current.Password, 8728, 1);

            var publicKey = Link.GetSecurePublicKey(Credentials.Current.Host);

            using (var link = Link.ConnectSecure(Credentials.Current.Host, Credentials.Current.Username, Credentials.Current.Password, publicKey)) {
                Assert.False(link.IsDisposed);
            }
        }

        [Fact]
        public void GetPublicKey_BadHost() {
            Assert.Throws<SocketException>(() => {
                Link.GetSecurePublicKey(Credentials.Current.Host + "BAD");
            });
        }
    }
}
