using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace tik4net
{
    public interface ITikReSentence: ITikSentence
    {
        string GetResponseField(string fieldName);
        bool TryGetResponseField(string fieldName, out string fieldValue);
        string GetResponseFieldOrDefault(string fieldName, string defaultValue);
    }
}
