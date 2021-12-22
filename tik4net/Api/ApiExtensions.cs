using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace tik4net.Api
{
    /// <summary>
    /// Connection/command extensions specific for <see cref="ApiConnection"/>.
    /// </summary>
    public static class ApiExtensions
    {
        #region -- Connection extensions --

        /// <summary>
        /// Factory method - creates parameters instance specific for connection and command type. Shortcut for a .proplist parameter.
        /// </summary>
        /// <param name="connection">The mikrotik connection.</param>
        /// <param name="proplist">Names of the wanted properties</param>
        /// <returns>Created parameter with name .proplist and a comma separated property list as value.</returns>
        /// <seealso cref="ITikCommand.Parameters"/>
        public static ITikCommandParameter CreateProplistParameter(this ITikConnection connection, params string[] proplist)
        {
            var result = connection.CreateParameter(TikSpecialProperties.Proplist, string.Join(",", proplist), TikCommandParameterFormat.NameValue);
            return result;
        }

        #endregion

        #region --- Command extensions --
        /// <summary>
        /// Adds new instance of parameter with .proplist to <see cref="ITikCommand.Parameters"/> list.
        /// </summary>
        /// <param name="command">The mikrotik command.</param>
        /// <param name="proplist">Names of the wanted properties</param>
        /// <returns>Instance of added parameter.</returns>
        public static ITikCommandParameter AddProplistParameter(this ITikCommand command, params string[] proplist)
        {
            ITikCommandParameter result = command.AddParameter(TikSpecialProperties.Proplist, string.Join(",", proplist), TikCommandParameterFormat.NameValue);
            return result;
        }
        #endregion

    }
}
