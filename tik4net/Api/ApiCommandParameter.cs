using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace tik4net.Api
{
    internal class ApiCommandParameter : ITikCommandParameter
    {
        private string _name;
        private string _value;
        private TikCommandParameterFormat _parameterFormat;

        public string Name
        {
            get { return _name; }
            set { _name = value; }
        }

        public string Value
        {
            get { return _value; }
            set { _value = value; }
        }

        public TikCommandParameterFormat ParameterFormat
        {
            get { return _parameterFormat; }
            set { _parameterFormat = value; }
        }

        public ApiCommandParameter()
        {

        }

        public ApiCommandParameter(string name)
        {
            Guard.ArgumentNotNullOrEmptyString(name, "name");

            _name = name;
        }

        public ApiCommandParameter(string name, string value)
            :this(name)
        {
            _value = value;
        }

        public ApiCommandParameter(string name, string value, TikCommandParameterFormat parameterFormat)
            : this(name, value)
        {
            _parameterFormat = parameterFormat;
        }

        public override string ToString()
        {
            return string.Format("{0}={1}", Name, Value);            
        }
    }
}
