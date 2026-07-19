// ProtocolModels.cs — Shared data models for protocol PoC tests
// Extracted from WinboxM2CatalogTest.cs.

namespace tik4net.integrationtests
{
    internal sealed class CatalogEntry
    {
        public long   Crc     { get; set; }
        public long   Size    { get; set; }
        public string Name    { get; set; }
        public string Unique  { get; set; }
        public string Version { get; set; }
        public override string ToString()
            => $"{Name,-32} v{Version,-10}  size={Size,8}B  crc={Crc}";
    }

    internal sealed class SystemInfo
    {
        public string Version      { get; set; }
        public string Board        { get; set; }
        public string Architecture { get; set; }
        public string Identity     { get; set; }
        public override string ToString()
            => $"{Board} ({Architecture}) RouterOS {Version} [{Identity}]";
    }

    internal sealed class InterfaceEntry
    {
        public int    Index    { get; set; }
        public string Flags    { get; set; }
        public string Name     { get; set; }
        public string Type     { get; set; }
        public bool   Running  => Flags?.Contains("R") == true;
        public bool   Disabled => Flags?.Contains("X") == true;
        public override string ToString()
            => $"[{Index}] {Name,-20} {Type,-15} {(Running ? "R" : "-")}{(Disabled ? "X" : "-")}";
    }
}
