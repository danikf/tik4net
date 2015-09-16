using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace tik4net
{
    /// <summary>
    /// Helper class to implement string fuctions from newer .NET - to support .NET 3.5 build.
    /// </summary>
    public static class StringHelper
    {
        /// <summary>
        /// Indicates whether the specified string is null or an System.String.Empty string or whitespace.
        /// NOTE: string.IsNullOrWhiteSpace with support for .NET 3.5
        /// </summary>
        /// <param name="str">The string to test.</param>
        /// <returns> true if the value parameter is null or an empty string ("") or whitespace; otherwise, false.</returns>
        public static bool IsNullOrWhiteSpace(this string str)
        {
#if V35
            if (string.IsNullOrEmpty(str))
                return true;
            else if (string.IsNullOrEmpty(str.Trim()))
                return true;
            else
                return false;
#else
            return string.IsNullOrWhiteSpace(str);
#endif
        }
    }
}