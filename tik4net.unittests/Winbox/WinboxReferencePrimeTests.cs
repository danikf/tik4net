using System;
using System.Collections.Generic;
using System.Threading;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using tik4net.Winbox;

namespace tik4net.unittests.Winbox
{
    /// <summary>
    /// Which referenced tables the WinBox-native decode path reads ahead of a synchronous decode
    /// (<see cref="WinboxRecordCodec.PrimeReferencesAsync"/>).
    /// </summary>
    /// <remarks>
    /// The prefetch exists so an awaited command never blocks its thread on a name lookup, and it must fetch
    /// EXACTLY the tables the decode would have fetched by itself — no more. Fetching one extra is not a wasted
    /// round trip but a wrong answer later: the id → name map is cached for the connection's lifetime, so a
    /// table read before a record exists renders that record as its bare numeric id ever after. That is precisely
    /// what a first version did, by predicting the tables from the <c>.jg</c> field map instead of asking the
    /// decoder: printing <c>/interface/list</c> — whose own rows carry <c>include</c>/<c>exclude</c> references
    /// to interface lists — froze the map, and every interface-list member added afterwards read back as a
    /// number (integration test <c>AddInterfaceListMemberWillNotFail</c>).
    /// <para>These are counted round trips, not a live router: the stub channel records whether the prefetch
    /// spoke at all.</para>
    /// </remarks>
    [TestClass]
    public class WinboxReferencePrimeTests
    {
        private const int RefKey = 0x10005;
        private static readonly int[] RefHandler = { 20, 90 };

        private static WinboxJgField ReferenceField(string uiType)
            => new WinboxJgField("list", RefKey, "u32", false, uiType: uiType, refHandler: RefHandler);

        private static Dictionary<int, Tuple<string, object>> Row(object value)
            => new Dictionary<int, Tuple<string, object>> { [RefKey] = Tuple.Create("u32", value) };

        private static int RoundTripsToPrime(WinboxJgField field, object value)
        {
            var channel = new CountingChannel();
            var codec = new WinboxRecordCodec(new WinboxNativeM2Operations(channel), null);
            codec.PrimeReferencesAsync(
                    new List<Dictionary<int, Tuple<string, object>>> { Row(value) },
                    new Dictionary<int, string> { [RefKey] = field.ApiName },
                    new Dictionary<int, WinboxJgField> { [RefKey] = field },
                    CancellationToken.None)
                .GetAwaiter().GetResult();
            return channel.SendReceiveCount;
        }

        [TestMethod]
        public void AResolvableReferenceIsFetchedAhead()
        {
            // The point of the prefetch: this row WILL ask for a name while decoding, so the table has to be
            // read here — under await — rather than from the thread running the decode.
            Assert.AreEqual(1, RoundTripsToPrime(ReferenceField("enm"), 33554432u),
                "a numeric reference value resolves to a name, so its table must be read ahead");
        }

        [TestMethod]
        public void AReferenceFieldThatResolvesNothingIsNotFetched()
        {
            // Every one of these carries a declared reference field, and not one of them ends in a lookup:
            // an empty list has no ids in it, and a value that is not a number is never looked up (it falls
            // back to the raw text — see WinboxNonNumericDecodeTests). Reading the table anyway would cache
            // it, and the cache is what a later record then falls out of.
            Assert.AreEqual(0, RoundTripsToPrime(ReferenceField("multinumber"), "[]"),
                "an empty reference list names nothing, so nothing may be fetched for it");
            Assert.AreEqual(0, RoundTripsToPrime(ReferenceField("enm"), "not-a-number"),
                "an unreadable reference value is never resolved, so nothing may be fetched for it");
            Assert.AreEqual(0, RoundTripsToPrime(ReferenceField("enm"), null),
                "an absent value resolves nothing");
        }

        [TestMethod]
        public void AFieldWhoseUiTypeRendersBeforeTheReferenceIsNotFetched()
        {
            // A .jg field can declare a reference AND a UI type that renders the value on its own (an address,
            // a flag set). The decoder returns from the typed branch and never consults the reference — so the
            // predicted-from-.jg approach fetched a table the decode provably does not use.
            Assert.AreEqual(0, RoundTripsToPrime(ReferenceField("ipaddr"), 3232235777u),
                "an ipaddr renders from the value itself; its declared reference is never consulted");
            var flags = new WinboxJgField("list", RefKey, "u32", false,
                enumMap: new Dictionary<int, string> { [0] = "established" }, uiType: "set",
                refHandler: RefHandler);
            Assert.AreEqual(0, RoundTripsToPrime(flags, 1u),
                "a flag set renders from its own bit map; its declared reference is never consulted");
        }

        /// <summary>
        /// An <see cref="IWinboxM2Channel"/> that counts requests and answers nothing. A prefetch that decides
        /// it needs a table is caught by the count; the empty answer then fails the getall, which the prefetch
        /// swallows exactly as it swallows an unreadable table on a real router.
        /// </summary>
        private sealed class CountingChannel : IWinboxM2Channel
        {
            internal int SendReceiveCount;

            public bool IsEncrypted => true;
            public bool DataAvailable => false;
            public bool SupportsStaleDrain => false;
            public bool SendAbandoned => false;
            public bool SendStalled => false;
            public bool SupportsReaderLoop => false;

            public byte[] SendReceive(byte[] m2, int timeoutMs)
            {
                SendReceiveCount++;
                return null;
            }

            public void Open(string host, int port, string user, string password, int connectTimeoutMs, int ioTimeoutMs)
                => throw new NotSupportedException();
            public byte[] NextReqIdField() => new byte[0];
            public void Send(byte[] m2) => throw new NotSupportedException();
            public byte[] Receive(int timeoutMs) => null;
            public byte[] ReceiveNextFrame() => throw new NotSupportedException();
            public void StartIdleServicing() { }
            public void Dispose() { }
        }
    }
}
