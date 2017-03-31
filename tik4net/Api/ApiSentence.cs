using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace tik4net.Api
{
    internal abstract class ApiSentence: ITikSentence
    {
        private readonly Dictionary<string, string> _words = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase); // <fieldName, value>

#if V35 || V40
        public IDictionary<string,string> Words
#else
        public IReadOnlyDictionary<string,string> Words
#endif
        {
            get { return _words; }
        }

        public string Tag
        {
            get { return GetWordValueOrDefault(TikSpecialProperties.Tag, ""); }
        }

        public ApiSentence(IEnumerable<string> words)
        {
            Regex keyValueRegex = new Regex("^=?(?<KEY>[^=]+)=(?<VALUE>.+)$", RegexOptions.Singleline);
            foreach(string word in words)
            {
                Match match = keyValueRegex.Match(word);
                if (match.Success)
                {
                    string key = match.Groups["KEY"].Value;
                    string value = match.Groups["VALUE"].Value;
                    if (!_words.ContainsKey(key))
                        _words.Add(key, value);
                    else if (_words[key] != value)
                        throw new TikSentenceException(string.Format("Duplicit key '{0}' with deffirent values '{1}' vs. '{2}'", key, _words[key], value) , this);
                    //else - duplicit key but the same value -> OK (workaround mikrotik bug?)
                }
            }
        }

        protected bool TryGetWordValue(string wordName, out string value)
        {
            //Regex keyValueRegex = new Regex("^=?" + wordName.Replace(".", @"\.") +"=(?<VALUE>.+)$");
            //foreach (string row in _words)
            //{
            //    Match regexMatch = keyValueRegex.Match(row);
            //    if (regexMatch.Success)
            //    {
            //        value = regexMatch.Groups["VALUE"].Value;
            //        return true;
            //    }
            //}
            return _words.TryGetValue(wordName, out value);
        }

        protected string GetWordValueOrDefault(string wordName, string defaultValue)
        {
            string result;
            if (TryGetWordValue(wordName, out result))
                return result;
            else
                return defaultValue;
        }

        protected string GetWordValue(string wordName)
        {
            string result;
            if (TryGetWordValue(wordName, out result))
                return result;
            else
                throw new TikSentenceException(string.Format("Missing word with name '{0}'.", wordName), this);
        }

        public override string ToString()
        {
            return GetType().Name + ":" + string.Join("|", _words.Select(w => string.Format("{0}={1}", w.Key, w.Value)).ToArray());
        }
    }
}
