namespace InvertedTomato.TikLink {
    public enum DataType : byte {
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