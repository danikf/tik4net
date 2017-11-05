using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Reflection;
using System.Linq;
using InvertedTomato.TikLink.RosDataTypes;

namespace InvertedTomato.TikLink {
    public static class RecordReflection {
        private class RecordMeta {
            public RosRecordAttribute Attribute { get; set; }
            public List<PropertyMeta> Properties { get; set; } = new List<PropertyMeta>();
        }
        private class PropertyMeta {
            public RosPropertyAttribute Attribute { get; set; }
            public PropertyInfo PropertyInfo { get; set; }
            public Type ValueType { get; set; }
            public TypeInfo ValueTypeInfo { get; set; }
            public IRosDataType RosDataType { get; set; }
        }
        private static ConcurrentDictionary<Type, RecordMeta> MetaRecords = new ConcurrentDictionary<Type, RecordMeta>();

        public static string GetPath<T>() {
            // Get metadata
            var meta = GetGenerateMeta<T>();

            return meta.Attribute.Path;
        }

        /// <summary>
        /// Set all RouterOS properties from a provided dictionary.
        /// </summary>
        public static void SetRosProperties<T>(T record, Dictionary<string, string> attributes) {
            if (null == record) {
                throw new ArgumentNullException(nameof(record));
            }
            if (null == attributes) {
                throw new ArgumentNullException(nameof(attributes));
            }

            // Get metadata
            var meta = GetGenerateMeta<T>();

            // Set all properties
            foreach (var property in meta.Properties) {
                if (attributes.TryGetValue(property.Attribute.RosName, out var mtvalue)) {
                    var localvalue = property.RosDataType.Decode(mtvalue, property.ValueType);
                    property.PropertyInfo.SetValue(record, localvalue);
                }
            }
        }

        /// <summary>
        /// Get all RouterOS Properties as a dictionary, excluding read-only.
        /// </summary>
        public static Dictionary<string, string> GetWritableRosProperties<T>(T record) {
            if (null == record) {
                throw new ArgumentNullException(nameof(record));
            }

            // Get metadata
            var meta = GetGenerateMeta<T>();

            // Get all properties
            var output = new Dictionary<string, string>();
            foreach (var property in meta.Properties) {
                // Skip read-only properties
                if (property.Attribute.IsReadOnly) {
                    continue;
                }
                
                var localvalue = property.PropertyInfo.GetValue(record);

                string mtvalue = property.RosDataType.Encode(localvalue, property.ValueType);
                
                output[property.Attribute.RosName] = mtvalue;
            }
            return output;
        }

        public static string ResolveProperty<T>(string name) {
            if (null == name) {
                throw new ArgumentNullException(nameof(name));
            }

            // Get metadata
            var meta = GetGenerateMeta<T>();

            // Get property
            var property = meta.Properties.SingleOrDefault(a => a.PropertyInfo.Name == name);
            if (null == property) {
                throw new KeyNotFoundException();
            }

            // Return field
            return property.Attribute.RosName;
        }

        private static RecordMeta GetGenerateMeta<T>() {
            var type = typeof(T);

            // If not in cache...
            if (!MetaRecords.TryGetValue(type, out var meta)) {
                meta = new RecordMeta();

                // Check record attribute
                meta.Attribute = type.GetTypeInfo().GetCustomAttribute<RosRecordAttribute>();
                if (null == meta.Attribute) {
                    throw new ArgumentException("Not decorated with TikRecordAttribute.");
                }

                // Build lookup of property infos
                foreach (var property in type.GetRuntimeProperties()) {
                    var propertyAttribute = property.GetCustomAttribute<RosPropertyAttribute>();
                    if (null == propertyAttribute) {
                        continue;
                    }

                    // Create meta record for property
                    meta.Properties.Add(new PropertyMeta() {
                        Attribute = propertyAttribute,
                        PropertyInfo = property,
                        ValueType = property.PropertyType,
                        ValueTypeInfo = property.PropertyType.GetTypeInfo(),
                        RosDataType = (IRosDataType)Activator.CreateInstance(propertyAttribute.RosDataType)
                    });
                }

                // Add to cache
                MetaRecords[type] = meta;
            }

            return meta;
        }
    }
}
