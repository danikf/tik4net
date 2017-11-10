namespace InvertedTomato.TikLink.Records {
    public abstract class SetRecordBase : RecordBase {
        /// <summary>
        /// Primary identifier
        /// </summary>
        [RosProperty(".id", IsRequired = true)]
        public string Id { get; set; }
    }
}