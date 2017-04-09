using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace tik4net
{
    /// <summary>
    /// IEnumerable extensions.
    /// </summary>
    public static class LinqEx
    {
        /// <summary>
        ///  Creates a Dictionary from an IEnumerable
        ///  according to a specified keySelector function.
        /// </summary>
        public static Dictionary<TKey, TValue> ToDictionaryEx<TKey, TValue>(this IEnumerable<TValue> values, Func<TValue, TKey> keySelector)
        {
            var result = new Dictionary<TKey, TValue>();

            foreach (TValue value in values)
            {
                try
                {
                    result.Add(keySelector(value), value);
                }
                catch (ArgumentException ex)
                {
                    throw new ArgumentException(string.Format("Could not add item with key '{0}': {1}", keySelector(value), ex.Message, ex));
                }
            }
            return result;
        }

        /// <summary>
        ///  Creates a Dictionary from an IEnumerable
        ///  according to a specified keySelector and valueSelector functions.
        /// </summary>
        public static Dictionary<TKey, TValue> ToDictionaryEx<TItem, TKey, TValue>(this IEnumerable<TItem> values, Func<TItem, TKey> keySelector, Func<TItem, TValue> valueSelector)
        {
            var result = new Dictionary<TKey, TValue>();

            foreach (TItem value in values)
            {
                try
                {
                    result.Add(keySelector(value), valueSelector(value));
                }
                catch (ArgumentException ex)
                {
                    throw new ArgumentException(string.Format("Could not add item with key '{0}': {1}", keySelector(value), ex.Message, ex));
                }
            }
            return result;
        }
    }
}
