namespace tik4net.Rest
{
    internal class RestCommandParameter : ITikCommandParameter
    {
        public string Name { get; set; }
        public string Value { get; set; }
        public TikCommandParameterFormat ParameterFormat { get; set; }

        internal RestCommandParameter(string name, string value, TikCommandParameterFormat format = TikCommandParameterFormat.Default)
        {
            Name = name;
            Value = value;
            ParameterFormat = format;
        }

        public override string ToString() => $"{Name}={Value}";
    }
}
