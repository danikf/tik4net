using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Reflection;

namespace InvertedTomato.TikLink {
    public static class RecordReflection {
        private class MetaRecord {
            public TikRecordAttribute RecordAttribute { get; set; }
            public Dictionary<string, TikPropertyAttribute> PropertyAttributes { get; set; }
            public Dictionary<string, PropertyInfo> PropertyInfos { get; set; }
        }
        private static ConcurrentDictionary<Type, MetaRecord> MetaRecords = new ConcurrentDictionary<Type, MetaRecord>();

        public static string GetPath<T>() {
            // Get metadata
            var meta = GetGenerateMeta<T>();

            return meta.RecordAttribute.Path;
        }

        public static void SetProperties<T>(T record, Dictionary<string, string> attributes) {
            if (null == record) {
                throw new ArgumentNullException(nameof(record));
            }
            if (null == attributes) {
                throw new ArgumentNullException(nameof(attributes));
            }

            // Get metadata
            var meta = GetGenerateMeta<T>();

            // Set all properties
            foreach (var item in meta.PropertyInfos) {
                if (attributes.TryGetValue(item.Key, out var value)) {
                    item.Value.SetValue(record, value);
                }
            }
        }

        public static Dictionary<string, string> GetProperties<T>(T record) {
            if (null == record) {
                throw new ArgumentNullException(nameof(record));
            }

            // Get metadata
            var meta = GetGenerateMeta<T>();

            // Get all properties
            var output = new Dictionary<string, string>();
            foreach (var item in meta.PropertyInfos) {
                output[item.Key] = (string)item.Value.GetValue(record);
            }
            return output;
        }

        private static MetaRecord GetGenerateMeta<T>() {
            var type = typeof(T);

            // If not in cache...
            if (!MetaRecords.TryGetValue(type, out var meta)) {
                meta = new MetaRecord();

                // Check record attribute
                meta.RecordAttribute = type.GetTypeInfo().GetCustomAttribute<TikRecordAttribute>();
                if (null == meta.RecordAttribute) {
                    throw new ArgumentException("Not decorated with TikRecordAttribute.");
                }

                // Build lookup of property infos
                meta.PropertyAttributes = new Dictionary<string, TikPropertyAttribute>();
                meta.PropertyInfos = new Dictionary<string, PropertyInfo>();
                foreach (var property in type.GetRuntimeProperties()) {
                    var propertyAttribute = property.GetCustomAttribute<TikPropertyAttribute>();
                    if (null == propertyAttribute) {
                        continue;
                    }

                    meta.PropertyAttributes[propertyAttribute.FieldName] = propertyAttribute;
                    meta.PropertyInfos[propertyAttribute.FieldName] = property;
                }

                // Add to cache
                MetaRecords[type] = meta;
            }

            return meta;
        }

    }
}
