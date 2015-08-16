using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace tik4net.Objects
{
    [TikEntity("/log", IsReadOnly = true)]
    public class Log
    {
        /// <summary>
        /// Row .id property.
        /// </summary>
        [TikProperty(".id", IsReadOnly = true, IsMandatory = true)]
        public string Id { get; private set; }

        /// <summary>
        /// Row message property.
        /// </summary>
        [TikProperty("message", IsReadOnly = true, IsMandatory = true)]
        public string Message { get; private set; }

        /// <summary>
        /// Row time property.
        /// </summary>
        [TikProperty("time", IsReadOnly = true, IsMandatory = true)]
        public string Time { get; private set; }

        /// <summary>
        /// Row topics property.
        /// </summary>
        [TikProperty("topics", IsReadOnly = true, IsMandatory = true)]
        public string Topics { get; private set; }
    }
}
