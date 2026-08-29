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

        public IReadOnlyDictionary<string,string> Words
        {
            get { return _words; }
        }

        public string Tag
        {
            get { return GetWordValueOrDefault(TikSpecialProperties.Tag, ""); }
        }

        private static readonly Regex keyValueRegex = new Regex("^=?(?<KEY>[^=]+)=(?<VALUE>.*)$", RegexOptions.Singleline | RegexOptions.Compiled);

        public ApiSentence(IEnumerable<string> words)
        {
            foreach(string word in words)
            {
                Match match = keyValueRegex.Match(word);
                if (match.Success)
                {
                    string key = match.Groups["KEY"].Value;
                    string value = match.Groups["VALUE"].Value;
                    
                    if (!_words.ContainsKey(key))
                        _words.Add(key, value);
                    else
                    {   //WORKAROUND
                        //REMARKS: there are mikrotik objects with multiple fields with the same name (e.q. /ip/ipsec/remote-peers)
                        //https://forum.mikrotik.com/viewtopic.php?f=9&t=99954&p=691864#p691858
                        int idx = 2;
                        while (_words.ContainsKey(key + idx))
                        {
                            idx++;
                        }
                        _words.Add(key + idx, value);
                    }
                    //The name above is INVENTED here, not sent by the router - see IsDuplicateWorkaroundName.
                    //if (_words[key] != value)
                    //    throw new TikSentenceException(string.Format("Duplicit key '{0}' with deffirent values '{1}' vs. '{2}'", key, _words[key], value) , this);
                    //else - duplicit key but the same value -> OK (workaround mikrotik bug?)
                }
            }
        }

        /// <summary>
        /// True when <paramref name="name"/> is one this class INVENTED for a word the router sent twice —
        /// the base name plus 2, 3, … — rather than a name the router used.
        /// </summary>
        /// <remarks>
        /// The rule lives next to the code that applies it, so the two cannot drift. Anything comparing this
        /// transport's vocabulary against another's has to subtract these: RouterOS sends <c>trusted</c>
        /// twice on a <c>/certificate</c> row, and counting the <c>trusted2</c> that comes out of it as a
        /// router field makes every other transport look one field short of the API. Same class of mistake
        /// as counting <c>.tag</c>, which is also ours and not the router's.
        /// </remarks>
        internal static bool IsDuplicateWorkaroundName(string name, ICollection<string> namesInSameSentence)
        {
            if (string.IsNullOrEmpty(name) || namesInSameSentence == null) return false;
            int end = name.Length;
            while (end > 0 && name[end - 1] >= '0' && name[end - 1] <= '9') end--;
            if (end == name.Length || end == 0) return false;      // no trailing digits, or all digits
            string digits = name.Substring(end);
            if (digits[0] == '0' || digits == "1") return false;   // the loop starts at 2 and never pads
            return namesInSameSentence.Contains(name.Substring(0, end));
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
            bool found = _words.TryGetValue(wordName, out var v);
            value = v!; // null only when found is false; callers check the return value before using 'value'
            return found;
        }

        [return: System.Diagnostics.CodeAnalysis.NotNullIfNotNull(nameof(defaultValue))]
        protected string? GetWordValueOrDefault(string wordName, string? defaultValue)
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
