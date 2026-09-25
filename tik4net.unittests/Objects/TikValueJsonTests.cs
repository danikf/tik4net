#nullable enable
#if NET8_0_OR_GREATER
// TikValueJsonTests.cs — System.Text.Json round trip of TikValue<T> (design review C4: without a converter the
// struct serialized its properties and deserialized as Absent, silently).

using System.Text.Json;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using tik4net.Objects;

namespace tik4net.unittests.Objects
{
    [TestClass]
    public class TikValueJsonTests
    {
        public class Dto
        {
            public TikValue<string?> Name { get; set; }
            public TikValue<int?> Port { get; set; }
            public TikValue<TikValueMapperTests.Mode?> Mode { get; set; }
            public TikValue<string?> Comment { get; set; }
            public TikValue<TikDuration?> Interval { get; set; }
            public TikValue<bool?> Flag { get; set; }
        }

        [TestMethod]
        public void EveryStateSurvivesARoundTrip()
        {
            var dto = new Dto
            {
                Name = "b",
                Port = 8080,
                Mode = TikValueMapperTests.Mode.Auto,
                Comment = TikValue<string?>.FromWire("x;y"),
                Interval = TikDuration.Parse("10s"),
                // Flag left Absent
            };

            string json = JsonSerializer.Serialize(dto);
            var back = JsonSerializer.Deserialize<Dto>(json)!;

            StringAssert.Contains(json, "\"Mode\":\"auto\"", "an enum travels as the router's word");
            StringAssert.Contains(json, "\"Comment\":{\"$raw\":\"x;y\"}");
            StringAssert.Contains(json, "\"Interval\":\"10s\"");
            StringAssert.Contains(json, "\"Flag\":null");
            Assert.IsTrue(back.Name == "b");
            Assert.IsTrue(back.Port == 8080);
            Assert.IsTrue(back.Mode == TikValueMapperTests.Mode.Auto);
            Assert.AreEqual(TikValueState.Unparsed, back.Comment.State);
            Assert.AreEqual("x;y", back.Comment.RawValue);
            Assert.AreEqual("10s", back.Interval.ToString());
            Assert.AreEqual(TikValueState.Absent, back.Flag.State);
        }

        [TestMethod]
        public void NullReadsBackAbsent_NeverAsAnUnset()
        {
            var back = JsonSerializer.Deserialize<Dto>("{\"Comment\":null}")!;

            Assert.AreEqual(TikValueState.Absent, back.Comment.State,
                "a deserialized entity must not unset a field on the strength of JSON");
        }

        [TestMethod]
        public void AnEnumWordTheEnumLacks_ReadsUnparsed()
        {
            var back = JsonSerializer.Deserialize<Dto>("{\"Mode\":\"ec2n155\"}")!;

            Assert.AreEqual(TikValueState.Unparsed, back.Mode.State);
            Assert.AreEqual("ec2n155", back.Mode.RawValue);
        }
    }
}
#endif
