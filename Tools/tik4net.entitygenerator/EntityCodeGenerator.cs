using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace tik4net.entitygenerator
{
    public static class EntityCodeGenerator
    {
        public static string Generate(string entityPath, string description, bool includeDetails, IEnumerable<ParsedProperty> properties)
        {
            StringBuilder source = new StringBuilder();
            source.AppendLine("\t" + @"/// <summary>");
            if (description.Contains("\n"))
            {
                source.AppendLine("\t" + string.Format(@"/// {0}", entityPath));
                foreach(string descrRow in description.Split(new string[] { "\n" }, StringSplitOptions.RemoveEmptyEntries))
                    source.AppendLine("\t" + string.Format(@"/// {0}", descrRow));
            }
            else
                source.AppendLine("\t" + string.Format(@"/// {0}: {1}", entityPath, description));

            source.AppendLine("\t" + @"/// </summary>");
            source.AppendLine(string.Format("\t[TikEntity(\"{0}\"{1})]", entityPath, includeDetails ? ", IncludeDetails = true" : ""));
            source.AppendLine(string.Format("\tpublic class {0}", "ENTITY_NAME"));
            source.AppendLine("\t{");

            foreach (var property in properties)
            {
                GenerateProperty(property, source);
            }

            source.AppendLine("\t}");

            return source.ToString();
        }

        private static void GenerateProperty(ParsedProperty property, StringBuilder source)
        {
            List<string> attrParams = new List<string>();
            attrParams.Add(string.Format("\"{0}\"", property.FieldName));
            if (property.IsReadOnly)
                attrParams.Add("IsReadOnly = true");
            if (property.IsMandatory)
                attrParams.Add("IsMandatory = true");
            if (!string.IsNullOrWhiteSpace(property.DefaultValue))
                attrParams.Add("DefaultValue = \"" + property.DefaultValue + "\"");
            if (property.UseUnset)
                attrParams.Add("UnsetOnDefault = true");

            source.AppendLine("\t\t" + @"/// <summary>");
            if (property.Description.Contains("\n"))
            {
                source.AppendLine("\t" + string.Format(@"/// {0}", property.FieldName));
                foreach (string descrRow in property.Description.Split(new string[] { "\n" }, StringSplitOptions.RemoveEmptyEntries))
                    source.AppendLine("\t" + string.Format(@"/// {0}", descrRow));
            }
            else
                source.AppendLine("\t\t" + string.Format(@"/// {0}: {1}", property.FieldName, property.Description));
            source.AppendLine("\t\t" + @"/// </summary>");
            source.AppendLine(string.Format("\t\t[TikProperty({0})]", string.Join(", ", attrParams)));
            source.AppendLine(string.Format("\t\tpublic {0} {1} *< get; {2}set; >*", 
                property.PropType, 
                property.PropertyName, 
                (property.IsReadOnly ? "private " : ""))
                .Replace("*<", "{").Replace(">*", "}"));
            source.AppendLine();
        }
    }
}
