namespace InvertedTomato.TikLink.Records {
    [RosRecord("/ip/service")]
    public class IpService : SetRecordBase {
        [RosProperty("name")] // Read-only
        public string Name { get; set; }

        [RosProperty("port")]
        public int Port { get; set; }

        [RosProperty("address")]
        public string Address { get; set; }

        [RosProperty("certificate")]
        public string Certificate { get; set; }

        [RosProperty("disabled")]
        public bool Disabled { get; set; }
    }
}
