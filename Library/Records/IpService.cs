namespace InvertedTomato.TikLink.Records {
    [RosRecord("/ip/service")]
    public class IpService : ISetRecord {
        /// <summary>
        /// Unique identifier
        /// </summary>
        [RosProperty(".id")]
        public string Id { get; set; }
        
        [RosProperty("name", IsReadOnly = true)]
        public string Name { get; set; }

        [RosProperty("port")]
        public int Port { get; set; }

        [RosProperty(".id")]
        public string Address { get; set; }

        [RosProperty("certificate")]
        public string Certificate { get; set; }

        [RosProperty("disabled")]
        public bool Disabled { get; set; }
    }
}
