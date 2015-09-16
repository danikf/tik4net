using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace tik4net
{
    /// <summary>
    /// How parameter will be formated in mikrotik request
    /// </summary>
    public enum TikCommandParameterFormat
    {
        /// <summary>
        /// Depends on <see cref="ITikCommand.DefaultParameterFormat"/> (or on method which is executed on command).
        /// </summary>
        Default,

        /// <summary>
        /// Format: ?name=value  (query).
        /// </summary>
        Filter,

        /// <summary>
        /// Format: =name=value (set, execute)
        /// </summary>
        NameValue,

        ///// <summary>
        ///// Format: =name (unset)
        ///// </summary>
        //NameOnly,
    }
}
