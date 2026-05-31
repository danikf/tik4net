namespace tik4net.Cli
{
    internal class CliCommandParameter : ITikCommandParameter
    {
        public string Name { get; set; }
        public string Value { get; set; }
        public TikCommandParameterFormat ParameterFormat { get; set; }

        internal CliCommandParameter(string name, string value, TikCommandParameterFormat format = TikCommandParameterFormat.Default)
        {
            Name = name;
            Value = value;
            ParameterFormat = format;
        }

        public override string ToString() => $"{Name}={Value}";
    }
}
