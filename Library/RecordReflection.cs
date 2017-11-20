using InvertedTomato.TikLink.Records;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;

namespace InvertedTomato.TikLink {
    public static class RecordReflection {
        private static Regex TimePattern = new Regex(@"^((\d+)d)?((\d+)h)?((\d+)m)?((\d+)s)?((\d+)ms)?$");
        private static Regex DateTimePattern = new Regex(@"^([a-z]{3})\/(\d{2})\/(\d{4}) (\d{2})\:(\d{2})\:(\d{2})$"); // nov/15/2017 09:08:21

        private class RecordMeta {
            public RosRecordAttribute Attribute { get; set; }
            public List<PropertyMeta> Properties { get; set; } = new List<PropertyMeta>();

            public override string ToString() {
                return Attribute?.Path;
            }
        }
        private class PropertyMeta {
            public RosPropertyAttribute Attribute { get; set; }
            public PropertyInfo PropertyInfo { get; set; }
            public Type ValueType { get; set; }
            public TypeInfo ValueTypeInfo { get; set; }

            public override string ToString() {
                return PropertyInfo.Name + "/" + Attribute?.RosName;
            }
        }
        private static ConcurrentDictionary<Type, RecordMeta> MetaRecords = new ConcurrentDictionary<Type, RecordMeta>();

        public static string GetPath<T>() where T : RecordBase {
            // Get metadata
            var meta = GetGenerateMeta<T>();

            return meta.Attribute.Path;
        }

        /// <summary>
        /// Set all RouterOS properties from a provided dictionary.
        /// </summary>
        public static void SetRosProperties<T>(T record, Dictionary<string, string> attributes) where T : RecordBase {
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
                if (attributes.TryGetValue(property.Attribute.RosName, out var rosValue)) {
                    object localValue = null;
                    if (property.ValueType == typeof(string)) { // RosString => string
                        if (rosValue == string.Empty) {
                            localValue = null;
                        } else {
                            localValue = rosValue;
                        }
                    } else if (property.ValueType == typeof(int?)) {// RosInteger => int?
                        if (rosValue == string.Empty) {
                            localValue = null;
                        } else if (int.TryParse(rosValue, out var intValue)) {
                            localValue = intValue;
                        } else {
                            throw new PropertyConverstionException($"Unable to parse '{rosValue}' as int? for '{property.Attribute.RosName}'");
                        }
                    } else if (property.ValueType == typeof(int)) {// RosInteger => int
                        if (rosValue == string.Empty) {
                            throw new PropertyConverstionException($"Unable to parse '{rosValue}' as int for '{property.Attribute.RosName}'");
                        } else if (int.TryParse(rosValue, out var intValue)) {
                            localValue = intValue;
                        } else {
                            throw new PropertyConverstionException($"Unable to parse '{rosValue}' as int for '{property.Attribute.RosName}'");
                        }
                    } else if (property.ValueType == typeof(long?)) { // RosInteger => long?
                        if (rosValue == string.Empty) {
                            localValue = null;
                        } else if (long.TryParse(rosValue, out var intValue)) {
                            localValue = intValue;
                        } else {
                            throw new PropertyConverstionException($"Unable to parse '{rosValue}' as long? for '{property.Attribute.RosName}'");
                        }
                    } else if (property.ValueType == typeof(long)) { // RosInteger => long
                        if (rosValue == string.Empty) {
                            throw new PropertyConverstionException($"Unable to parse '{rosValue}' as long for '{property.Attribute.RosName}'");
                        } else if (long.TryParse(rosValue, out var intValue)) {
                            localValue = intValue;
                        } else {
                            throw new PropertyConverstionException($"Unable to parse '{rosValue}' as long for '{property.Attribute.RosName}'");
                        }
                    } else if (property.ValueType == typeof(bool?)) { // RosBoolean => bool?
                        if (rosValue == string.Empty) {
                            localValue = null;
                        } else if (rosValue == "true" || rosValue == "yes") {
                            localValue = true;
                        } else if (rosValue == "false" || rosValue == "no") {
                            localValue = false;
                        } else {
                            throw new PropertyConverstionException($"Unable to parse '{rosValue}' as bool? for '{property.Attribute.RosName}'");
                        }
                    } else if (property.ValueType == typeof(bool)) { // RosBoolean => bool
                        if (rosValue == string.Empty) {
                            throw new PropertyConverstionException($"Unable to parse '{rosValue}' as bool for '{property.Attribute.RosName}'");
                        } else if (rosValue == "true" || rosValue == "yes") {
                            localValue = true;
                        } else if (rosValue == "false" || rosValue == "no") {
                            localValue = false;
                        } else {
                            throw new PropertyConverstionException($"Unable to parse '{rosValue}' as bool for '{property.Attribute.RosName}'");
                        }
                    } else if (property.ValueType == typeof(TimeSpan?)) { // RosTimeSpan => TimeSpan?
                        if (rosValue == string.Empty) {
                            localValue = null;
                        } else {
                            var match = TimePattern.Match(rosValue);
                            if (!match.Success) {
                                throw new PropertyConverstionException($"Unable to parse '{rosValue}' as TimeSpan? for '{property.Attribute.RosName}'");
                            }
                            int d = 0;
                            if (match.Groups[2].Value != string.Empty && !int.TryParse(match.Groups[2].Value, out d)) {
                                throw new PropertyConverstionException($"Unable to parse '{rosValue}' as TimeSpan? for '{property.Attribute.RosName}'");
                            }
                            int h = 0;
                            if (match.Groups[4].Value != string.Empty && !int.TryParse(match.Groups[4].Value, out h)) {
                                throw new PropertyConverstionException($"Unable to parse '{rosValue}' as TimeSpan? for '{property.Attribute.RosName}'");
                            }
                            int m = 0;
                            if (match.Groups[6].Value != string.Empty && !int.TryParse(match.Groups[6].Value, out m)) {
                                throw new PropertyConverstionException($"Unable to parse '{rosValue}' as TimeSpan? for '{property.Attribute.RosName}'");
                            }
                            int s = 0;
                            if (match.Groups[8].Value != string.Empty && !int.TryParse(match.Groups[8].Value, out s)) {
                                throw new PropertyConverstionException($"Unable to parse '{rosValue}' as TimeSpan? for '{property.Attribute.RosName}'");
                            }
                            int ms = 0;
                            if (match.Groups[10].Value != string.Empty && !int.TryParse(match.Groups[10].Value, out ms)) {
                                throw new PropertyConverstionException($"Unable to parse '{rosValue}' as TimeSpan? for '{property.Attribute.RosName}'");
                            }

                            localValue = new TimeSpan(d, h, m, s, ms);
                        }
                    } else if (property.ValueType == typeof(TimeSpan)) { // RosTimeSpan => TimeSpan
                        if (rosValue == string.Empty) {
                            throw new PropertyConverstionException($"Unable to parse '{rosValue}' as TimeSpan for '{property.Attribute.RosName}'");
                        } else {
                            var match = TimePattern.Match(rosValue);
                            if (!match.Success) {
                                throw new PropertyConverstionException($"Unable to parse '{rosValue}' as TimeSpan for '{property.Attribute.RosName}'");
                            }
                            int d = 0;
                            if (match.Groups[2].Value != string.Empty && !int.TryParse(match.Groups[2].Value, out d)) {
                                throw new PropertyConverstionException($"Unable to parse '{rosValue}' as TimeSpan for '{property.Attribute.RosName}'");
                            }
                            int h = 0;
                            if (match.Groups[4].Value != string.Empty && !int.TryParse(match.Groups[4].Value, out h)) {
                                throw new PropertyConverstionException($"Unable to parse '{rosValue}' as TimeSpan for '{property.Attribute.RosName}'");
                            }
                            int m = 0;
                            if (match.Groups[6].Value != string.Empty && !int.TryParse(match.Groups[6].Value, out m)) {
                                throw new PropertyConverstionException($"Unable to parse '{rosValue}' as TimeSpan for '{property.Attribute.RosName}'");
                            }
                            int s = 0;
                            if (match.Groups[8].Value != string.Empty && !int.TryParse(match.Groups[8].Value, out s)) {
                                throw new PropertyConverstionException($"Unable to parse '{rosValue}' as TimeSpan for '{property.Attribute.RosName}'");
                            }
                            int ms = 0;
                            if (match.Groups[10].Value != string.Empty && !int.TryParse(match.Groups[10].Value, out ms)) {
                                throw new PropertyConverstionException($"Unable to parse '{rosValue}' as TimeSpan for '{property.Attribute.RosName}'");
                            }

                            localValue = new TimeSpan(d, h, m, s, ms);
                        }
                    } else if (property.ValueType == typeof(DateTime?)) { // RosDateTime => DateTime?  nov/15/2017 09:08:21
                        if (rosValue == string.Empty) {
                            localValue = null;
                        } else {
                            if (!DateTime.TryParseExact(rosValue, "MMM/dd/yyyy HH:mm:ss", CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var d)) {
                                throw new PropertyConverstionException($"Unable to parse '{rosValue}' as DateTime? for '{property.Attribute.RosName}'");
                            }
                            localValue = d;
                        }
                    } else if (property.ValueType == typeof(DateTime)) { // RosDateTime => DateTime
                        if (rosValue == string.Empty) {
                            throw new PropertyConverstionException($"Unable to parse '{rosValue}' as DateTime for '{property.Attribute.RosName}'");
                        } else {
                            if (!DateTime.TryParseExact(rosValue, "MMM/dd/yyyy HH:mm:ss", CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var d)) {
                                throw new PropertyConverstionException($"Unable to parse '{rosValue}' as DateTime for '{property.Attribute.RosName}'");
                            }
                            localValue = d;
                        }
                    } else if (property.ValueTypeInfo.IsGenericType && property.ValueType.GetGenericTypeDefinition() == typeof(Nullable<>) && property.ValueTypeInfo.GenericTypeParameters[0].GetTypeInfo().IsEnum) { // RosEnum => Enum?
                        if (rosValue == string.Empty) {
                            localValue = null;
                        } else {
                            foreach (var field in property.ValueTypeInfo.GenericTypeParameters[0].GetRuntimeFields()) {
                                var attribute = field.GetCustomAttribute<RosEnumAttribute>(true);
                                if (attribute != null && attribute.Value == rosValue) {
                                    localValue = field.GetValue(null);
                                    break;
                                }
                            }
                            if (null == localValue) {
                                throw new PropertyConverstionException($"Unable to parse '{rosValue}' as Enum? for '{property.Attribute.RosName}'");
                            }
                        }
                    } else if (property.ValueTypeInfo.IsEnum) { // RosEnum => Enum
                        if (rosValue == string.Empty) {
                            throw new PropertyConverstionException($"Unable to parse '{rosValue}' as Enum for '{property.Attribute.RosName}'");
                        } else {
                            foreach (var field in property.ValueType.GetRuntimeFields()) {
                                var attribute = field.GetCustomAttribute<RosEnumAttribute>(true);
                                if (attribute != null && attribute.Value == rosValue) {
                                    localValue = field.GetValue(null);
                                    break;
                                }
                            }
                            if (null == localValue) {
                                throw new PropertyConverstionException($"Unable to parse '{rosValue}' as Enum for '{property.Attribute.RosName}'");
                            }
                        }
                    } else {
                        throw new PropertyConverstionException($"Data type '{property.ValueType.Name}' is not supported. '{property.Attribute.RosName}'");
                    }

                    // Set value on field
                    property.PropertyInfo.SetValue(record, localValue);

                    // Remove from inbound attributes
                    attributes.Remove(property.Attribute.RosName);
                }
            }

            record.OtherProperties = attributes;
        }

        /// <summary>
        /// Get all RouterOS Properties as a dictionary, excluding read-only and null.
        /// </summary>
        public static Dictionary<string, string> GetRosProperties<T>(T record) where T : RecordBase {
            if (null == record) {
                throw new ArgumentNullException(nameof(record));
            }

            // Get metadata
            var meta = GetGenerateMeta<T>();

            // Get all properties
            var rosValues = new Dictionary<string, string>();
            foreach (var property in meta.Properties) {
                // Skip read-only properties
                if (property.Attribute.IsReadOnly) {
                    continue;
                }

                // Get local value
                var localValue = property.PropertyInfo.GetValue(record);

                // Convert to ROS value
                if (property.ValueType == typeof(string)) { // string => RosString
                    if (localValue != null) {
                        rosValues[property.Attribute.RosName] = (string)localValue;
                    }
                } else if (property.ValueType == typeof(int?)) {// int? => RosInteger
                    if (localValue != null) {
                        rosValues[property.Attribute.RosName] = localValue.ToString();
                    }
                } else if (property.ValueType == typeof(int)) {// int => RosInteger
                    rosValues[property.Attribute.RosName] = localValue.ToString();
                } else if (property.ValueType == typeof(long?)) { // long? => RosInteger
                    if (localValue != null) {
                        rosValues[property.Attribute.RosName] = localValue.ToString();
                    }
                } else if (property.ValueType == typeof(long)) { // long => RosInteger
                    rosValues[property.Attribute.RosName] = localValue.ToString();
                } else if (property.ValueType == typeof(bool?)) { // bool => RosBoolean
                    if (localValue != null) {
                        rosValues[property.Attribute.RosName] = ((bool)localValue) ? "yes" : "no";
                    }
                } else if (property.ValueType == typeof(bool)) { // bool => RosBoolean
                    rosValues[property.Attribute.RosName] = ((bool)localValue) ? "yes" : "no";
                } else if (property.ValueType == typeof(TimeSpan?)) { // TimeSpan? => RosTimeSpan
                    if (localValue != null) {
                        rosValues[property.Attribute.RosName] = ((TimeSpan)localValue).ToString(@"d\dh\hm\ms\s");
                    }
                } else if (property.ValueType == typeof(TimeSpan)) { // TimeSpan => RosTimeSpan
                    rosValues[property.Attribute.RosName] = ((TimeSpan)localValue).ToString(@"d\dh\hm\ms\s");
                } else if (property.ValueType == typeof(DateTime?)) { // DateTime? => RosDateTime
                    if (localValue != null) {
                        rosValues[property.Attribute.RosName] = ((DateTime)localValue).ToString(@"MMM/dd/yyyy HH:mm:ss");
                    }
                } else if (property.ValueType == typeof(DateTime)) { // DateTime => RosDateTime
                    rosValues[property.Attribute.RosName] = ((DateTime)localValue).ToString(@"MMM/dd/yyyy HH:mm:ss");
                } else if (property.ValueTypeInfo.IsGenericType && property.ValueType.GetGenericTypeDefinition() == typeof(Nullable<>) && property.ValueType.GenericTypeArguments[0].GetTypeInfo().IsEnum) { // Enum? => RosEnum
                    if (localValue != null) {
                        foreach (var field in property.ValueTypeInfo.GenericTypeParameters[0].GetRuntimeFields()) { // TODO: What if the seleted enum doesn't have an attribute? Should throw exception
                            var attribute = field.GetCustomAttribute<RosEnumAttribute>(true);
                            if (attribute != null && field.Name == localValue.ToString()) {
                                rosValues[property.Attribute.RosName] = attribute.Value;
                            }
                        }
                    }
                } else if (property.ValueTypeInfo.IsEnum) { // enum => RosEnum
                    foreach (var field in property.ValueType.GetRuntimeFields()) { // TODO: What if the seleted enum doesn't have an attribute? Should throw exception
                        var attribute = field.GetCustomAttribute<RosEnumAttribute>(true);
                        if (attribute != null && field.Name == localValue.ToString()) {
                            rosValues[property.Attribute.RosName] = attribute.Value;
                        }
                    }
                } else {
                    throw new PropertyConverstionException($"Data type '{property.ValueType.Name}' is not supported. '{property.Attribute.RosName}'");
                }
            }
            return rosValues;
        }

        public static string ResolveProperty<T>(string name) where T : RecordBase {
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

        private static RecordMeta GetGenerateMeta<T>() where T : RecordBase {
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
                        //RosDataType = (IRosDataType)Activator.CreateInstance(propertyAttribute.RosDataType)
                    });
                }

                // Add to cache
                MetaRecords[type] = meta;
            }

            return meta;
        }
    }
}
