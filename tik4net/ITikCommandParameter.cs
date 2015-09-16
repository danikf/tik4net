using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace tik4net
{
    /// <summary>
    /// Named parameter used by <see cref="ITikCommand"/>.
    /// </summary>
    public interface ITikCommandParameter
    {
        /// <summary>
        /// Parameter name
        /// </summary>
        string Name { get; set; }

        /// <summary>
        /// Parameter value (formated to string as expected by <see cref="ITikConnection"/>).
        /// </summary>
        string Value { get; set; }

        /// <summary>
        /// Parameter specific format how will be parameter formated in mikrotik request.
        /// </summary>
        TikCommandParameterFormat ParameterFormat { get; set; }
    }
}
