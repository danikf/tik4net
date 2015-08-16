using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace tik4net
{
    public interface ITikCommand
    {
        ITikConnection Connection { get; set; }

        string CommandText { get; set; }

        List<ITikCommandParameter> Parameters { get; }

        void ExecuteNonQuery();

        string ExecuteScalar();

        ITikReSentence ExecuteSingleRow();

        IEnumerable<ITikReSentence> ExecuteList();

        void ExecuteAsync(Action<ITikReSentence> oneResponseCallback);

        void Cancel();
    }
}
