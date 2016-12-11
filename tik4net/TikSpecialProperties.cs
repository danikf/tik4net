using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace tik4net
{
    /// <summary>
    /// Names of well known properties (set of constants)
    /// </summary>
    public static class TikSpecialProperties
    {
        /// <summary>
        /// Id property = .id
        /// </summary>
        public const string Id = ".id";

        /// <summary>
        /// Proplist property = .proplist
        /// </summary>
        public const string Proplist = ".proplist";

        /// <summary>
        /// .tag property - used to correlate simultaneous command responses.
        /// </summary>
        public const string Tag = ".tag";

        /// <summary>
        /// value-name property used to set name of unset field in unset command
        /// </summary>
        public const string UnsetValueName = "value-name";

        /// <summary>
        /// Return value from =done sentence. See <see cref="ITikDoneSentence"/>
        /// </summary>
        public const string Ret = "ret";
    }
}
