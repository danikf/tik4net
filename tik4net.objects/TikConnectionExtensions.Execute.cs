using System;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using tik4net.Objects.Tracking;

namespace tik4net.Objects
{
    public static partial class TikConnectionExtensions
    {
        /// <summary>
        /// Executes given <paramref name="commandText"/> on router and ensures that operation was successful.
        /// </summary>
        /// <param name="connection">Tik connection used to load.</param>
        /// <param name="commandText">Command text</param>
        /// <param name="parameters">Optional list of parameters</param>
        /// <exception cref="InvalidOperationException">Connection or command text not set. Command is already running. Connection is not opened. Invalid response from API.</exception>
        /// <exception cref="TikCommandTrapException">!trap returned from API call.</exception>
        /// <exception cref="TikCommandFatalException">!fatal returned from API call.</exception>
        /// <exception cref="TikCommandUnexpectedResponseException">Unexpected response from mikrotik (multiple returned rows, missing !done row etc.)</exception>
        /// <exception cref="TikNoSuchCommandException">Invalid mikrotik command (syntax error). Mikrotik API message: 'no such command'</exception>
        /// <exception cref="TikNoSuchItemException">Invalid item (bad id/name etc.). Mikrotik API message: 'no such item'.</exception>
        /// <exception cref="TikAlreadyHaveSuchItemException">Duplicate item (a duplicate id, name, …). Mikrotik API message: 'already have such item'.</exception>
        public static void ExecuteNonQuery(this ITikConnection connection, string commandText, params ITikCommandParameter[] parameters)
        {
            var command = connection.CreateCommand(commandText, parameters);
            command.ExecuteNonQuery();
        }

        /// <summary>
        /// Executes given <paramref name="commandText"/> on router and ensures that operation returns one value (=ret parameter), which is returned as result.
        /// </summary>
        /// <param name="connection">Tik connection used to load.</param>
        /// <param name="commandText">Command text</param>
        /// <param name="parameters">Optional list of parameters</param>
        /// <returns>Value returned by router.</returns>
        /// <inheritdoc cref="ExecuteNonQuery(ITikConnection, string, ITikCommandParameter[])" path="/exception"/>
        public static string ExecuteScalar(this ITikConnection connection, string commandText, params ITikCommandParameter[] parameters)
        {
            var command = connection.CreateCommand(commandText, parameters);
            return command.ExecuteScalar();
        }
    }
}
