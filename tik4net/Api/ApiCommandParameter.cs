using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace tik4net.Api
{
    public class ApiCommandParameter: ITikCommandParameter
    {
        private string _name;
        private string _value;

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

        public override string ToString()
        {
            return string.Format("{0}={1}", Name, Value);
        }
    }
}
