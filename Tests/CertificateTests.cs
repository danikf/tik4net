using InvertedTomato.TikLink;
using InvertedTomato.TikLink.Records;
using System;
using System.Collections.Generic;
using System.Text;
using Xunit;
using System.Linq;

namespace Tests {
    public class CertificateTests {
        [Fact]
        public void A() {
            using (var link = Link.Connect(Credentials.Current.Host, Credentials.Current.Username, Credentials.Current.Password)) {
                // Create certificate
                var obj1 = new SystemCertificate() {
                    Name = "test",
                    CommonName = "test",
                    DaysValid = 1,
                    //KeyUsage = "tls-server";
                    //Trusted = true
                };
                link.System.Certificate.Add(obj1);

                // Check certificate was created correctly
                var objs1 = link.System.Certificate.Query(new Dictionary<string, string>(){
                    {nameof(SystemCertificate.Name), $"={obj1.Name}" }
                }, null);
                Assert.Equal(1, objs1.Count);
                var obj2 = objs1.Single();
                Assert.Equal(obj1.Name, obj2.Name);
                Assert.Equal(obj1.CommonName, obj2.CommonName);
                Assert.Equal(obj1.DaysValid, obj2.DaysValid);

                // Sign certificate
                link.System.Certificate.Sign(obj2);

                // Check certificate was created correctly
                var objs2 = link.System.Certificate.Query(new Dictionary<string, string>(){
                    {nameof(SystemCertificate.Name), $"={obj1.Name}" }
                }, null);
                Assert.Equal(1, objs2.Count);
                var obj3 = objs2.Single();
                Assert.Equal(obj1.Name, obj3.Name);
                Assert.Equal(obj1.CommonName, obj3.CommonName);
                Assert.Equal(obj1.DaysValid, obj3.DaysValid);

                // Delete certificate
                link.System.Certificate.Delete(obj2);
            }
        }
    }
}
