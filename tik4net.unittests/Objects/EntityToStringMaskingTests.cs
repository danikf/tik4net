using Microsoft.VisualStudio.TestTools.UnitTesting;
using tik4net.Objects;
using tik4net.Objects.Ppp;

namespace tik4net.unittests.Objects
{
    /// <summary>
    /// <see cref="TikEntityObjectsExtensions.EntityToString{TEntity}"/> is documented for logging, so a field the metadata
    /// marks <c>IsSensitive</c> prints as <c>***</c> when it has a value (security review D3).
    /// </summary>
    [TestClass]
    public class EntityToStringMaskingTests
    {
        [TestMethod]
        public void ASensitiveFieldWithAValuePrintsAsStars()
        {
            var secret = new PppSecret { Name = "t4n-user", Password = "S3cretPw!" };

            string text = secret.EntityToString();

            Assert.IsFalse(text.Contains("S3cretPw!"), text);
            StringAssert.Contains(text, "  password=***");
            StringAssert.Contains(text, "  name=t4n-user", "the other fields still print");
        }

        [TestMethod]
        public void ASensitiveFieldWithNoValuePrintsEmpty()
        {
            // Empty says "no password" truthfully; *** would claim one.
            string text = new PppSecret { Name = "t4n-user" }.EntityToString();

            StringAssert.Contains(text, "  password=\r\n".Replace("\r\n", System.Environment.NewLine));
        }
    }
}
