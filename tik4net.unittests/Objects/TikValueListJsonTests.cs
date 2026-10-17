#nullable enable
#if NET8_0_OR_GREATER
// TikValueListJsonTests.cs — System.Text.Json round trip of a TikValueList<T> field: an array of items, each in the
// router's spelling (a negated TikNegatableValue member with its '!') and {"$raw":"word"} for a word the type lacks, so
// nothing is lost and no '!' is read into a word or out of one.

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
            public TikNegatableField<TikValueList<TikPortRange>?> Ports { get; set; }
            public TikField<TikValueList<TikNegatableValue<TikValueListMapperTests.Hotspot>>?> Hotspot { get; set; }
        }

        [TestMethod]
        public void ItemsAndBothNegations_SurviveARoundTrip()
        {
            var dto = new Dto
            {
                Ports = TikValueList<TikPortRange>.Not(22, new TikPortRange(1000, 2000)),
                Hotspot = new TikValueList<TikNegatableValue<TikValueListMapperTests.Hotspot>>(
                    TikNegatableValue<TikValueListMapperTests.Hotspot>.Not(TikValueListMapperTests.Hotspot.FromClient),
                    (TikNegatableValue<TikValueListMapperTests.Hotspot>)TikValueListMapperTests.Hotspot.Http,
                    TikValue<TikNegatableValue<TikValueListMapperTests.Hotspot>>.FromWire("!newer-kind")),
            };

            string json = JsonSerializer.Serialize(dto);
            var back = JsonSerializer.Deserialize<Dto>(json)!;

            StringAssert.Contains(json, "\"Ports\":{\"$not\":[\"22\",\"1000-2000\"]}");
            StringAssert.Contains(json, "\"Hotspot\":[\"!from-client\",\"http\",{\"$raw\":\"!newer-kind\"}]");
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
            Assert.AreEqual("!80", back.Ports.Value![0].RawValue);
            Assert.IsFalse(back.Ports.IsNegated);
        }
    }
}
#endif
