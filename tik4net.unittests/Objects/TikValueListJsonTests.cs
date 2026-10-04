#nullable enable
#if NET8_0_OR_GREATER
// TikValueListJsonTests.cs — System.Text.Json round trip of a TikValueList<T> field: an array of items, each a plain value
// in the router's spelling, {"$not":…} for a negated one and {"$raw":"word"} for a word the type lacks — the same
// conventions as TikField<T>, so nothing is lost and no '!' is read into a word or out of one.

using System.Text.Json;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using tik4net.Objects;

namespace tik4net.unittests.Objects
{
    [TestClass]
    public class TikValueListJsonTests
    {
        public class Dto
        {
            public TikField<TikValueList<TikPortRange>?> Ports { get; set; }
            public TikField<TikValueList<TikValueListMapperTests.Hotspot>?> Hotspot { get; set; }
        }

        [TestMethod]
        public void ItemsAndBothNegations_SurviveARoundTrip()
        {
            var dto = new Dto
            {
                Ports = TikValue<TikValueList<TikPortRange>?>.Not(new TikValueList<TikPortRange>(22, new TikPortRange(1000, 2000))),
                Hotspot = new TikValueList<TikValueListMapperTests.Hotspot>(
                    TikValue<TikValueListMapperTests.Hotspot>.Not(TikValueListMapperTests.Hotspot.FromClient),
                    TikValueListMapperTests.Hotspot.Http,
                    TikValue<TikValueListMapperTests.Hotspot>.FromWire("newer-kind")),
            };

            string json = JsonSerializer.Serialize(dto);
            var back = JsonSerializer.Deserialize<Dto>(json)!;

            StringAssert.Contains(json, "\"Ports\":{\"$not\":[\"22\",\"1000-2000\"]}");
            StringAssert.Contains(json, "\"Hotspot\":[{\"$not\":\"from-client\"},\"http\",{\"$raw\":\"newer-kind\"}]");
            Assert.AreEqual(dto.Ports, back.Ports);
            Assert.AreEqual(dto.Hotspot, back.Hotspot);
            Assert.IsTrue(back.Hotspot.Value![2].IsWord);
        }

        [TestMethod]
        public void AWordThatLooksNegated_StaysAWord()
        {
            var dto = new Dto { Ports = new TikValueList<TikPortRange>(TikValue<TikPortRange>.FromWire("!80")) };

            var back = JsonSerializer.Deserialize<Dto>(JsonSerializer.Serialize(dto))!;

            Assert.IsTrue(back.Ports.Value![0].IsWord);
            Assert.IsFalse(back.Ports.Value![0].IsNegated);
            Assert.AreEqual("!80", back.Ports.Value![0].RawValue);
        }
    }
}
#endif
