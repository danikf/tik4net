using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace tik4net
{
    /// <summary>
    /// Mikrotik Connection. Main object to access mikrotik router.
    /// Implementation of interface depends on technology that 
    /// is used to access mikrotik (API, SSH, TELNET, ...).
    /// <example>
    /// using(ITikConnection connection = ConnectionFactory.OpenConnection(TikConnectionType.Api, "192.168.1.1", "user", "pass"))
    /// {
    ///     // ... do work ... 
    ///     // ... do query ...
    ///     Connection.Close();
    /// }
    /// </example>
    /// </summary>
    public interface ITikConnection: IDisposable
    {
        /// <summary>
        /// Gets a value indicating whether is logged on (<see cref="Open(string, int, string, string)"/>).
        /// </summary>
        /// <value><c>true</c> if is logged on; otherwise, <c>false</c>.</value>
        bool IsOpened { get; }

        event EventHandler<string> OnReadRow;
        event EventHandler<string> OnWriteRow;

        /// <summary>
        /// Opens connection to the specified mikrotik host on default port (depends on technology) and perform the logon operation.
        /// </summary>
        /// <param name="host">The host.</param>
        /// <param name="user">The user.</param>
        /// <param name="password">The password.</param>
        /// <seealso cref="Close"/>
        void Open(string host, string user, string password);

        /// <summary>
        /// Opens connection to the specified mikrotik host on specified port and perform the logon operation.
        /// </summary>
        /// <param name="host">The host (name or ip).</param>
        /// <param name="port">TCPIP port.</param>
        /// <param name="user">The user.</param>
        /// <param name="password">The password.</param>
        /// <seealso cref="Close"/>
        void Open(string host, int port, string user, string password);

        /// <summary>
        /// Performs the logoff operation and closes connection. Called also via Dispose of connector.
        /// </summary>
        /// <seealso cref="Open(string, int, string, string)"/>
        void Close();

        ITikCommand CreateCommand();

        ITikCommand CreateCommand(string commandText, params ITikCommandParameter[] parameters);

        ITikCommandParameter CreateParameter(string name, string value);

        IEnumerable<ITikSentence> CallCommandSync(IEnumerable<string> commandRows);

        string CallCommandAsync(IEnumerable<string> commandRows, string tag, Action<ITikSentence> oneResponseCallback);
    }
}
