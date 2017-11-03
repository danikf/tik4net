using InvertedTomato.TikLink;
using System.Net.Sockets;
using Xunit;

namespace Tests {
    public class ConnectTests {
        [Fact]
        public void Connect_Success() {
            var link = Link.Connect(Credentials.Current.Host);
            Assert.False(link.IsDisposed);
            link.Dispose();
            Assert.True(link.IsDisposed);
        }

        [Fact]
        public void Connect_Fail() {
            Assert.Throws<SocketException>(() => {
                Link.Connect(Credentials.Current.Host + "BAD");
            });
        }

        /*
        [Fact]
        public void ConnectSecure_Success() {
            using (var link = Link.ConnectSecure(Credentials.Current.Host)) {
                Assert.False(link.IsDisposed);
            }
        }*/
    }
}
