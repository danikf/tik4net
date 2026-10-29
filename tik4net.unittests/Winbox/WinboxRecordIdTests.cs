// WinboxRecordIdTests.cs — the *HEX record handle, parsed and formatted in one place (WinBox + MAC review, §4 C16).

using Microsoft.VisualStudio.TestTools.UnitTesting;
using tik4net.Winbox;

namespace tik4net.unittests.Winbox
{
    [TestClass]
    public class WinboxRecordIdTests
    {
        [DataTestMethod]
        [DataRow("*1A", 0x1A)]
        [DataRow("*0", 0)]
        [DataRow("*80000019", unchecked((int)0x80000019))]   // a route: the high bit is read as bits
        [DataRow("*FFFFFFFA", unchecked((int)0xFFFFFFFA))]   // /queue/type's pcq-upload-default
        public void AHandle_ParsesToItsBits(string text, int expected)
        {
            Assert.IsTrue(WinboxRecordId.TryParse(text, out int id));
            Assert.AreEqual(expected, id);
            Assert.AreEqual(text, WinboxRecordId.Format(unchecked((uint)id)));
        }

        [DataTestMethod]
        [DataRow(null)]
        [DataRow("")]
        [DataRow("*")]
        [DataRow("ether1")]
        [DataRow("*xyz")]
        [DataRow("*100000000")]   // wider than a u32
        public void ANameOrNothing_IsNotAHandle(string text)
            => Assert.IsFalse(WinboxRecordId.TryParse(text, out _));

        [TestMethod]
        public void TheDecodersWideForm_FormatsTheSame()
            => Assert.AreEqual(WinboxRecordId.Format(0xFFFFFFFAu), WinboxRecordId.Format((ulong)0xFFFFFFFAu));
    }
}
