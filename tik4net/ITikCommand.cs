using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace tik4net
{
    /// <summary>
    /// Provides ADO.NET like api to mikrotik router. Should be used inside of opened <seealso cref="ITikConnection"/>.s
    /// </summary>
    /// <seealso cref="ITikConnection"/>
    /// <seealso cref="TikCommandException"/>
    public interface ITikCommand
    {
        /// <summary>
        /// Connection assigned to command (used to perform operations on router).
        /// </summary>
        ITikConnection Connection { get; set; }

        /// <summary>
        /// Comnmand send to router (in mikrotik API format).
        /// </summary>
        string CommandText { get; set; }

        /// <summary>
        /// Parameters of command (without '=').
        /// </summary>
        List<ITikCommandParameter> Parameters { get; }

        /// <summary>
        /// True if =detail= command extension should be added to comamnd request
        /// </summary>
        bool IncludeDetails { get; set; }

        /// <summary>
        /// Excecutes given <see cref="CommandText"/> on router and ensures that operation was sucessfull.
        /// </summary>
        void ExecuteNonQuery();

        /// <summary>
        /// Executes given <see cref="CommandText"/> on router and ensures that operation returns one value (=ret parameter), which is returned as result.
        /// </summary>
        /// <returns>Value returned by router.</returns>
        string ExecuteScalar();

        /// <summary>
        /// Executes given <see cref="CommandText"/> on router and ensures that operation returns exactly one row (1x !re and 1x !done) as result.
        /// </summary>
        /// <returns>Content of !re sentence.</returns>
        ITikReSentence ExecuteSingleRow();

        /// <summary>
        /// Executes given <see cref="CommandText"/> on router and returns all result sentences (all !re sentences) as result.
        /// </summary>
        /// <returns>List of !re sentences</returns>
        IEnumerable<ITikReSentence> ExecuteList();

        /// <summary>
        /// Calls given <see cref="CommandText"/> to router. Response is returned via <paramref name="oneResponseCallback"/> callback when it is read from mikrotik (for tag, which has been dynamically assigned).
        /// REMARKS: <paramref name="oneResponseCallback"/> is called from another NON-GUI thread. If you want to show response in UI, 
        /// you should use some kind of synchronization like BeginInvoke in WinForms or SynchronizationContext. You can not touch UI controls directly without it.
        /// </summary>
        /// <param name="oneResponseCallback">Callback called periodically when response sentence is read from mikrotik.</param>
        /// <seealso cref="Cancel"/>
        /// <seealso cref="ITikReSentence"/>
        void ExecuteAsync(Action<ITikReSentence> oneResponseCallback);

        /// <summary>
        /// Adds new instance of parameter to <see cref="Parameters"/> list.
        /// </summary>
        /// <param name="name">Parameter name.</param>
        /// <param name="value">Parameter value</param>
        /// <returns></returns>
        ITikCommandParameter AddParameter(string name, string value);

        /// <summary>
        /// Adds newly created instances of <see cref="ITikCommand.Parameters"/>.
        /// </summary>
        /// <param name="parameterNamesAndValues">Name and value of parameters for command. (name, value, name2, value2, ..., name9, value9, ...)</param>
        /// <returns>List of created parameters.</returns>
        IEnumerable<ITikCommandParameter> AddParameterAndValues(params string[] parameterNamesAndValues);

        /// <summary>
        /// Cancells already running async command (should be called on the same instance of <see cref="ITikCommand"/> on which <see cref="ExecuteAsync"/> has been called).
        /// </summary>
        /// <seealso cref="ExecuteAsync(Action{ITikReSentence})"/>
        void Cancel();
    }
}
