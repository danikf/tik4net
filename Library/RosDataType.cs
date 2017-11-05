namespace InvertedTomato.TikLink {
    public enum RosDataType : byte {
        String,
        Integer,
        Decimal,
        Boolean,
        Enum,

        Id,
        Duration,
        MacAddress,
        IPAddress,
        IPAddressWithMask
    }
}