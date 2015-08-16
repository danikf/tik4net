using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace tik4net.Api
{
    internal abstract class ApiSentence: ITikSentence
    {
        private readonly string[] _words;

        public string Tag
        {
            get { return GetWordValueOrDefault(".tag", ""); }
        }

        public ApiSentence(IEnumerable<string> words)
        {
            _words = words.ToArray();
        }

        protected bool TryGetWordValue(string wordName, out string value)
        {            
            Regex keyValueRegex = new Regex("^=?" + wordName.Replace(".", @"\.") +"=(?<VALUE>.+)$");
            foreach (string row in _words)
            {
                Match regexMatch = keyValueRegex.Match(row);
                if (regexMatch.Success)
                {
                    value = regexMatch.Groups["VALUE"].Value;
                    return true;
                }
            }

            value = null;
            return false;
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
            return GetType().Name + ":" + string.Join("|", _words);
        }
    }
}
