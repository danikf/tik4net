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
        /// Parameter name. 
        /// REMARKS: If starts with one of ?= character, than <see cref="ParameterFormat"/> is ignored.
        /// </summary>
        string Name { get; set; }

        /// <summary>
        /// Parameter value (formated to string as expected by <see cref="ITikConnection"/>).
        /// </summary>
        string Value { get; set; }

        /// <summary>
        /// Parameter specific format how will be parameter formated in mikrotik request.
        /// REMARKS: This value is ignored if <see cref="Name"/> starts with one of ?= characters
        /// </summary>
        TikCommandParameterFormat ParameterFormat { get; set; }
    }
}
