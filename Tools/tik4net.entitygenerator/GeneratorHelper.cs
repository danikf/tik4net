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
            if (name == TikSpecialProperties.Id)
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

        public static string DetermineFieldTypeFromDocumentation(string documentationFiledType, string fieldName, bool isReadOnly)
        {
            if (fieldName == TikSpecialProperties.Id)
                return "string";
            else if (fieldName == "disabled" || fieldName == "invalid" || fieldName == "active" || fieldName == "dynamic")
                return "bool";
            else if (fieldName == "comment")
                return "string";

            else if (string.Equals(documentationFiledType, "string", StringComparison.OrdinalIgnoreCase))
                return "string";
            //else if (string.Equals(documentationFiledType, "IP/netmask", StringComparison.OrdinalIgnoreCase))
            //    return "Ipv4Address";
            //else if (string.Equals(documentationFiledType, "IP", StringComparison.OrdinalIgnoreCase))
            //    return "Ipv4Address";
            //else if (string.Equals(documentationFiledType, "MAC", StringComparison.OrdinalIgnoreCase))
            //    return "MacAddress";
            else if (string.Equals(documentationFiledType.Replace(" ",  ""), "yes|no", StringComparison.OrdinalIgnoreCase))
                return "bool";
            else if (string.Equals(documentationFiledType, "integer", StringComparison.OrdinalIgnoreCase))
                return "int";
            else if (string.IsNullOrWhiteSpace(documentationFiledType))
                return "string";
            else if (isReadOnly)
                return "string"; //string fallback for R/O properties - strong typed property is not necessary
            else
                return "string" + "/*" + documentationFiledType + "*/";
        }

        public static bool DetermineFieldReadOnly(string name, string value)
        {
            if (name == TikSpecialProperties.Id)
                return true;
            else if (name == "invalid" || name == "dynamic")
                return true;

            return false;
        }

        public static bool DetermineFieldMandatory(string name, string value)
        {
            if (name == TikSpecialProperties.Id || name == "name")
                return true;
            else if (name == "comment")
                return false;

            if (string.IsNullOrEmpty(value))
                return false;

            return false;
        }
    }
}
