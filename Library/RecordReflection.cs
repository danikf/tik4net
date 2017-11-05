using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Reflection;
using System.Linq;
using InvertedTomato.TikLink.MTEncodings;

namespace InvertedTomato.TikLink {
    public static class RecordReflection {
        private class RecordMeta {
            public TikRecordAttribute Attribute { get; set; }
            public List<PropertyMeta> Properties { get; set; } = new List<PropertyMeta>();
        }
        private class PropertyMeta {
            public TikPropertyAttribute Attribute { get; set; }
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
            foreach (var property in meta.Properties) {
                if (attributes.TryGetValue(property.Attribute.Name, out var mtvalue)) {
                    object localvalue;
                    switch (property.Attribute.DataType) {
                        case DataType.String:
                            localvalue = mtvalue;
                            break;
                        case DataType.Integer:
                            if (property.Attribute.IsRequired) {
                                localvalue = IntegerEncoding.Decode(mtvalue);
                            } else {
                                localvalue = IntegerEncoding.DecodeNullable(mtvalue);
                            }
                            break;
                        case DataType.Decimal:
                            if (property.Attribute.IsRequired) {
                                localvalue = DecimalEncoding.Decode(mtvalue);
                            } else {
                                localvalue = DecimalEncoding.DecodeNullable(mtvalue);
                            }
                            break;
                        case DataType.Boolean:
                            if (property.Attribute.IsRequired) {
                                localvalue = BooleanEncoding.Decode(mtvalue);
                            } else {
                                localvalue = BooleanEncoding.DecodeNullable(mtvalue);
                            }
                            break;
                        case DataType.Enum:
                            if (property.Attribute.IsRequired) {
                                localvalue = EnumEncoding.Decode(mtvalue, property.ValueType);
                            } else {
                                localvalue = EnumEncoding.DecodeNullable(mtvalue, property.ValueType);
                            }
                            break;

                        case DataType.Id:
                            localvalue = mtvalue;
                            break;
                        case DataType.Duration:
                            if (property.Attribute.IsRequired) {
                                localvalue = DurationEncoding.Decode(mtvalue);
                            } else {
                                localvalue = DurationEncoding.DecodeNullable(mtvalue);
                            }
                            break;
                        case DataType.MacAddress:
                            localvalue = mtvalue;
                            break;
                        case DataType.IPAddress:
                            localvalue = mtvalue;
                            break;
                        case DataType.IPAddressWithMask:
                            localvalue = mtvalue;
                            break;
                        default:
                            throw new NotImplementedException();
                    }

                    property.PropertyInfo.SetValue(record, localvalue);
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
            foreach (var property in meta.Properties) {
                var localvalue = property.PropertyInfo.GetValue(record);
                string mtvalue;

                switch (property.Attribute.DataType) {
                    case DataType.String:
                        mtvalue = (string)localvalue;
                        break;
                    case DataType.Integer:
                        if (property.Attribute.IsRequired) {
                            mtvalue = IntegerEncoding.Encode((long)localvalue);
                        } else {
                            mtvalue = IntegerEncoding.EncodeNullable((long?)localvalue);
                        }
                        break;
                    case DataType.Decimal:
                        if (property.Attribute.IsRequired) {
                            mtvalue = DecimalEncoding.Encode((double)localvalue);
                        } else {
                            mtvalue = DecimalEncoding.EncodeNullable((double?)localvalue);
                        }
                        break;
                    case DataType.Boolean:
                        if (property.Attribute.IsRequired) {
                            mtvalue = BooleanEncoding.Encode((bool)localvalue);
                        } else {
                            mtvalue = BooleanEncoding.EncodeNullable((bool?)localvalue);
                        }
                        break;
                    case DataType.Enum:
                        if (property.Attribute.IsRequired) {
                            mtvalue = EnumEncoding.Encode((Enum)localvalue);
                        } else {
                            mtvalue = EnumEncoding.EncodeNullable((Enum)localvalue);
                        }
                        break;

                    case DataType.Id:
                        mtvalue = (string)localvalue;
                        break;
                    case DataType.Duration:
                        if (property.Attribute.IsRequired) {
                            mtvalue = DurationEncoding.Encode((double)localvalue);
                        } else {
                            mtvalue = DurationEncoding.EncodeNullable((double?)localvalue);
                        }
                        break;
                    case DataType.MacAddress:
                        mtvalue = (string)localvalue;
                        break;
                    case DataType.IPAddress:
                        mtvalue = (string)localvalue;
                        break;
                    case DataType.IPAddressWithMask:
                        mtvalue = (string)localvalue;
                        break;
                    default:
                        throw new NotImplementedException();
                }


                output[property.Attribute.Name] = (string)mtvalue;  // TODO: Naieve!!!
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
                meta.Attribute = type.GetTypeInfo().GetCustomAttribute<TikRecordAttribute>();
                if (null == meta.Attribute) {
                    throw new ArgumentException("Not decorated with TikRecordAttribute.");
                }

                // Build lookup of property infos
                foreach (var property in type.GetRuntimeProperties()) {
                    var propertyAttribute = property.GetCustomAttribute<TikPropertyAttribute>();
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
