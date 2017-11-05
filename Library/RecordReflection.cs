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
                if (attributes.TryGetValue(property.Attribute.Name, out var mtvalue)) {
                    object localvalue;
                    switch (property.Attribute.RosData) {
                        case RosDataType.String:
                            localvalue = mtvalue;
                            break;
                        case RosDataType.Integer:
                            if (property.Attribute.IsRequired) {
                                localvalue = IntegerEncoding.Decode(mtvalue);
                            } else {
                                localvalue = IntegerEncoding.DecodeNullable(mtvalue);
                            }
                            break;
                        case RosDataType.Decimal:
                            if (property.Attribute.IsRequired) {
                                localvalue = DecimalEncoding.Decode(mtvalue);
                            } else {
                                localvalue = DecimalEncoding.DecodeNullable(mtvalue);
                            }
                            break;
                        case RosDataType.Boolean:
                            if (property.Attribute.IsRequired) {
                                localvalue = BooleanEncoding.Decode(mtvalue);
                            } else {
                                localvalue = BooleanEncoding.DecodeNullable(mtvalue);
                            }
                            break;
                        case RosDataType.Enum:
                            if (property.Attribute.IsRequired) {
                                localvalue = EnumEncoding.Decode(mtvalue, property.ValueType);
                            } else {
                                localvalue = EnumEncoding.DecodeNullable(mtvalue, property.ValueType);
                            }
                            break;

                        case RosDataType.Id:
                            localvalue = mtvalue;
                            break;
                        case RosDataType.Duration:
                            if (property.Attribute.IsRequired) {
                                localvalue = DurationEncoding.Decode(mtvalue);
                            } else {
                                localvalue = DurationEncoding.DecodeNullable(mtvalue);
                            }
                            break;
                        case RosDataType.MacAddress:
                            localvalue = mtvalue;
                            break;
                        case RosDataType.IPAddress:
                            localvalue = mtvalue;
                            break;
                        case RosDataType.IPAddressWithMask:
                            localvalue = mtvalue;
                            break;
                        default:
                            throw new NotImplementedException();
                    }

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
                string mtvalue;

                switch (property.Attribute.RosData) {
                    case RosDataType.String:
                        mtvalue = (string)localvalue;
                        break;
                    case RosDataType.Integer:
                        if (property.Attribute.IsRequired) {
                            mtvalue = IntegerEncoding.Encode((long)localvalue);
                        } else {
                            mtvalue = IntegerEncoding.EncodeNullable((long?)localvalue);
                        }
                        break;
                    case RosDataType.Decimal:
                        if (property.Attribute.IsRequired) {
                            mtvalue = DecimalEncoding.Encode((double)localvalue);
                        } else {
                            mtvalue = DecimalEncoding.EncodeNullable((double?)localvalue);
                        }
                        break;
                    case RosDataType.Boolean:
                        if (property.Attribute.IsRequired) {
                            mtvalue = BooleanEncoding.Encode((bool)localvalue);
                        } else {
                            mtvalue = BooleanEncoding.EncodeNullable((bool?)localvalue);
                        }
                        break;
                    case RosDataType.Enum:
                        if (property.Attribute.IsRequired) {
                            mtvalue = EnumEncoding.Encode((Enum)localvalue);
                        } else {
                            mtvalue = EnumEncoding.EncodeNullable((Enum)localvalue);
                        }
                        break;

                    case RosDataType.Id:
                        mtvalue = (string)localvalue;
                        break;
                    case RosDataType.Duration:
                        if (property.Attribute.IsRequired) {
                            mtvalue = DurationEncoding.Encode((double)localvalue);
                        } else {
                            mtvalue = DurationEncoding.EncodeNullable((double?)localvalue);
                        }
                        break;
                    case RosDataType.MacAddress:
                        mtvalue = (string)localvalue;
                        break;
                    case RosDataType.IPAddress:
                        mtvalue = (string)localvalue;
                        break;
                    case RosDataType.IPAddressWithMask:
                        mtvalue = (string)localvalue;
                        break;
                    default:
                        throw new NotImplementedException();
                }
                
                output[property.Attribute.Name] = mtvalue; 
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
            return property.Attribute.Name;
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
                        ValueTypeInfo = property.PropertyType.GetTypeInfo()
                    });
                }

                // Add to cache
                MetaRecords[type] = meta;
            }

            return meta;
        }
    }
}
