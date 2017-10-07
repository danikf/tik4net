using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace tik4net.Api
{
    internal class ApiReSentence : ApiSentence, ITikReSentence
    {
        public ApiReSentence(IEnumerable<string> words) 
            : base(words)
        {
        }

        public string GetId()
        {
            return GetResponseField(TikSpecialProperties.Id);
        }

        public string GetResponseField(string fieldName)
        {
            return GetWordValue(fieldName);
        }

        public string GetResponseFieldOrDefault(string fieldName, string defaultValue)
        {
            return GetWordValueOrDefault(fieldName, defaultValue);
        }

        public bool TryGetResponseField(string fieldName, out string fieldValue)
        {
            return TryGetWordValue(fieldName, out fieldValue);
        }
    }
}
