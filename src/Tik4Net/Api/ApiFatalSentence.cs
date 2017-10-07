using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Tik4Net.Api
{
    internal class ApiFatalSentence : ApiSentence
    {
        public string Message { get; private set; }

        public ApiFatalSentence(IEnumerable<string> words) 
            : base(words)
        {
            Message = string.Join("\n", words.ToArray());
        }
    }
}
