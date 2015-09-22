using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace tik4net.entitygenerator
{
    public class ParsedProperty
    {
        public string PropertyName { get; private set; }
        public string FieldName { get; private set; }
        public string Description { get; private set; }
        public string PropType { get; private set; }
        public bool IsReadOnly { get; private set; }
        public bool IsMandatory { get; private set; }
        public string DefaultValue { get; private set; }
        public bool UseUnset { get; set; }

        public ParsedProperty(string propertyName, string fieldName, string description, string propType, bool isReadOnly, bool isMandatory, string defaultValue)
        {
            PropertyName = propertyName;
            FieldName = fieldName;
            Description = description;
            PropType = propType;
            IsReadOnly = isReadOnly;
            IsMandatory = isMandatory;
            DefaultValue = defaultValue;
        }
    }
}
