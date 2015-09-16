using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace tik4net
{
    /// <summary>
    /// Helper class with methods used for checking arguments, type compatibility and so on.
    /// Main idea comes from MS-CAB.
    /// </summary>
    public static class Guard
    {
        /// <summary>
        /// Checks a string argument to ensure it isn't null or empty.
        /// </summary>
        /// <param name="argumentValue">The argument value to check.</param>
        /// <param name="argumentName">The name of the argument.</param>
        public static void ArgumentNotNullOrEmptyString(string argumentValue, string argumentName)
        {
            ArgumentNotNull(argumentValue, argumentName);

            if (argumentValue.Length == 0)
                throw new ArgumentException(string.Format(CultureInfo.CurrentCulture, "The provided String argument {0} must not be empty.", argumentName), argumentName);
        }

        /// <summary>
        /// Checks an argument to ensure it isn't null.
        /// </summary>
        /// <param name="argumentValue">The argument value to check.</param>
        /// <param name="argumentName">The name of the argument.</param>
        public static void ArgumentNotNull(object argumentValue, string argumentName)
        {
            if (argumentValue == null)
                throw new ArgumentNullException(argumentName);
        }

        /// <summary>
        /// Checks an argumen to ensure it is of given type.
        /// </summary>
        /// <typeparam name="TExpectedType">Expected type.</typeparam>
        /// <param name="argumentValue">The argument value to check.</param>
        /// <param name="argumentName">The name of the argument.</param>
        public static void ArgumentOfType<TExpectedType>(object argumentValue, string argumentName)
        {
            if (argumentName is TExpectedType)
                throw new ArgumentException(string.Format(CultureInfo.CurrentCulture, "The provided argument {0} must of '{1}' type.", argumentName, typeof(TExpectedType)), argumentName);            
        }
    }
}
