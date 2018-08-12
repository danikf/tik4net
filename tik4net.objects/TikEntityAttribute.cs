using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace tik4net.Objects
{
    /// <summary>
    /// Attribute that is used to decorate tik entity class.
    /// </summary>
    /// <seealso cref="TikConnectionExtensions"/>
    /// <seealso cref="TikEntityObjectsExtensions"/>
    /// <seealso cref="TikEntityAttribute"/>
    [AttributeUsage(AttributeTargets.Class, AllowMultiple = false)]
    public sealed class TikEntityAttribute : Attribute
    {
        /// <summary>
        /// Gets the entity path in API notation (/ip/firewall/mangle).
        /// </summary>
        /// <value>The entity path.</value>
        public string EntityPath { get; private set; }

        /// <summary>
        /// Sufix added to entity path when loading. eq. /print
        /// Default: /print
        /// </summary>
        public string LoadCommand { get; set; }

        /// <summary>
        /// Parameter format (when parameter itself is set to <see cref="TikCommandParameterFormat.Default"/>) during  load operation.
        /// Default: <see cref="TikCommandParameterFormat.Filter"/>.
        /// </summary>
        public TikCommandParameterFormat LoadDefaultParameneterFormat { get; set; }

        /// <summary>
        /// If the whole entity is R/O.
        /// </summary>
        public bool IsReadOnly { get; set; }

        /// <summary>
        /// If entity list is ordered (move operation does make sense)
        /// </summary>
        public bool IsOrdered { get; set; }

        /// <summary>
        /// If entity should be loaded with =detail= option.
        /// </summary>
        public bool IncludeDetails { get; set; }

        /// <summary>
        /// If entity fields should be listed explicitly via .proplist option.
        /// </summary>
        public bool IncludeProplist { get; set; }

        /// <summary>
        /// If entity exists in single instance.
        /// </summary>
        public bool IsSingleton { get; set; }

        /// <summary>
        /// Initializes a new instance of the <see cref="TikEntityAttribute"/> class.
        /// </summary>
        /// <param name="entityPath">The entity path in API notation (/ip/firewall/mangle).</param>
        /// <param name="loadCommand">Sufix added to entity path when loading. eq. /print</param>
        /// <param name="loadDefaultParameneterFormat">Parameter format (when parameter itself is set to <see cref="TikCommandParameterFormat.Default"/>) during  load operation.</param>
        /// <param name="isReadOnly">If the whole entity is R/O.</param>
        /// <param name="isOrdered">If entity list is ordered (move operation does make sense).</param>
        /// <param name="includeDetails">If entity should be loaded with =detail= option.</param>
        /// <param name="isSingleton">If entity exists in single instance</param>
        public TikEntityAttribute(string entityPath, string loadCommand, TikCommandParameterFormat loadDefaultParameneterFormat, bool isReadOnly, bool isOrdered, bool includeDetails, bool isSingleton)
        {
            Guard.ArgumentNotNullOrEmptyString(entityPath, "entityPath");
            EntityPath = entityPath;
            LoadCommand = loadCommand;
            LoadDefaultParameneterFormat = loadDefaultParameneterFormat;
            IsReadOnly = isReadOnly;
            IsOrdered = isOrdered;
            IncludeDetails = includeDetails;
            IsSingleton = isSingleton;            
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="TikEntityAttribute"/> class. ReadOnly = false, IncludeDetails=false.
        /// </summary>
        /// <param name="entityPath">The entity path in API notation (/ip/firewall/mangle).</param>
        public TikEntityAttribute(string entityPath)
        {
            Guard.ArgumentNotNullOrEmptyString(entityPath, "entityPath");
            EntityPath = entityPath;
            LoadCommand = "/print";
            LoadDefaultParameneterFormat = TikCommandParameterFormat.Filter;

            IsReadOnly = false;
            IsOrdered = false;
            IncludeDetails = false;
            IncludeProplist = false;
            IsSingleton = false;
        }
    }

}
