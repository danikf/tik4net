using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace tik4net.entitygenerator
{
    public static class GeneratorHelper
    {
        public static string Camelize(string str)
        {
            string result = CultureInfo.CurrentCulture.TextInfo.ToTitleCase(str);
            result = result.Replace("-", "");
            result = result.Replace(".", "");
            return result;
        }

        public static string DetermineFieldType(string name, string value)
        {
            if (name == ".id")
                return "string";
            else if (name == "disabled" || name == "invalid" || name == "active" || name == "dynamic")
                return "bool";
            else if (name == "comment")
                return "string";

            if (value.Equals("true", StringComparison.OrdinalIgnoreCase)
                || (value.Equals("false", StringComparison.OrdinalIgnoreCase))
                || (value.Equals("yes", StringComparison.OrdinalIgnoreCase))
                || (value.Equals("no", StringComparison.OrdinalIgnoreCase))
                )
                return "bool";
            else
            {
                long tmp;
                if (long.TryParse(value, out tmp))
                {
                    return "long";
                }
                else
                    return "string";
            }
        }

        public static bool DetermineFieldReadOnly(string name, string value)
        {
            if (name == ".id")
                return true;
            else if (name == "invalid" || name == "dynamic")
                return true;

            return false;
        }

        public static bool DetermineFieldMandatory(string name, string value)
        {
            if (name == ".id" || name == "name")
                return true;
            else if (name == "comment")
                return false;

            if (string.IsNullOrEmpty(value))
                return false;

            return false;
        }
    }
}
