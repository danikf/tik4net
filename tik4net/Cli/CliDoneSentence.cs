using System.Collections.Generic;

namespace tik4net.Cli
{
    internal class CliDoneSentence : ITikDoneSentence
    {
        private readonly string _ret;

        internal CliDoneSentence(string ret = null)
        {
            _ret = ret;
        }

        public IReadOnlyDictionary<string, string> Words
        {
            get
            {
                var d = new Dictionary<string, string>();
                if (_ret != null)
                    d[TikSpecialProperties.Ret] = _ret;
                return d;
            }
        }

        public string Tag => string.Empty;

        public string GetResponseWord()
        {
            if (_ret == null)
                throw new TikSentenceException("No return value in CLI !done.", this);
            return _ret;
        }

        public string GetResponseWordOrDefault(string defaultValue) => _ret ?? defaultValue;
    }
}
