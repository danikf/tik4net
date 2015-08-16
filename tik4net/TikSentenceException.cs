using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using tik4net.Api;

namespace tik4net
{
    public class TikSentenceException : Exception
    {
        private ITikSentence _sentence;

        public TikSentenceException(string message, ITikSentence sentecne) 
            : base(message)
        {
            _sentence = sentecne;
        }
    }
}
