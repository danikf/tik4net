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
    internal static class Guard
    {
        /// <summary>
        /// Checks a string argument to ensure it isn't null or empty.
        /// </summary>
        /// <param name="argumentValue">The argument value to check.</param>
        /// <param name="argumentName">The name of the argument.</param>
        internal static void ArgumentNotNullOrEmptyString(string argumentValue, string argumentName)
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
        internal static void ArgumentNotNull(object argumentValue, string argumentName)
        {
            if (argumentValue == null)
                throw new ArgumentNullException(argumentName);
        }

        /// <summary>
        /// Checks a name/value list (<c>name, value, name2, value2, …</c>) for an unpaired last name, which would
        /// otherwise be dropped without a word. Called before anything of the list is added.
        /// </summary>
        /// <param name="namesAndValues">The list to check.</param>
        /// <param name="argumentName">The name of the argument.</param>
        internal static void NamesAndValuesArePaired(string[] namesAndValues, string argumentName)
        {
            if (namesAndValues.Length % 2 != 0)
                throw new ArgumentException(string.Format(CultureInfo.InvariantCulture,
                    "Parameters come in name/value pairs; '{0}' has no value.",
                    namesAndValues[namesAndValues.Length - 1]), argumentName);
        }

        /// <summary>
        /// Checks an argument to ensure it is of the given type. A null is not checked here: call
        /// <see cref="ArgumentNotNull"/> first.
        /// </summary>
        /// <typeparam name="TExpectedType">Expected type.</typeparam>
        /// <param name="argumentValue">The argument value to check.</param>
        /// <param name="argumentName">The name of the argument.</param>
        /// <exception cref="ArgumentException"><paramref name="argumentValue"/> is not a <typeparamref name="TExpectedType"/>.</exception>
        internal static void ArgumentOfType<TExpectedType>(object argumentValue, string argumentName)
        {
            if (!(argumentValue is TExpectedType))
                throw new ArgumentException(string.Format(CultureInfo.InvariantCulture,
                    "The provided argument {0} must be of '{1}' type; it is '{2}'.", argumentName,
                    typeof(TExpectedType).Name, argumentValue?.GetType().FullName ?? "null"), argumentName);
        }
    }
}
