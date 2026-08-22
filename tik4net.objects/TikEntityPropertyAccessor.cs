using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Text;

namespace tik4net.Objects
{
    /// <summary>
    /// Accessor to one tik entity property.
    /// </summary>
    /// <seealso cref="TikPropertyAttribute"/>
    /// <seealso cref="TikEntityMetadata"/>
    public sealed class TikEntityPropertyAccessor
    {
        private readonly TikEntityMetadata _owner;
        private bool _isReadOnly;

        /// <summary>
        /// Name of the property in C# code of the entity.
        /// </summary>
        public string PropertyName { get; private set; }

        /// <summary>
        /// Type of the property in C# code of the entity.
        /// </summary>
        public Type PropertyType { get; private set; }

        /// <summary>
        /// The type actually converted to and from the wire: <see cref="PropertyType"/>, or the type inside it
        /// when the property is a <see cref="Nullable{T}"/>.
        /// </summary>
        public Type ValueType { get; private set; }

        /// <summary>
        /// True when the property can hold <c>null</c> as a value distinct from any of its values — i.e. it is
        /// a <see cref="Nullable{T}"/>.
        /// </summary>
        /// <remarks>
        /// This is the difference that lets the mapper tell "the caller said nothing about this field" from
        /// "the caller asked for the value that happens to be the default". On a non-nullable property the two
        /// are the same state, so one of them is always misread — see <c>MapperValueSemanticsTests</c>.
        /// </remarks>
        public bool IsNullable { get; private set; }

        /// <summary>
        /// Name of the field in mikrotik router.
        /// </summary>
        /// <seealso cref="TikPropertyAttribute.FieldName"/>
        public string FieldName { get; private set; }

        /// <summary>
        /// If property (and mikrotik field) is R/O.
        /// </summary>
        /// <seealso cref="TikPropertyAttribute.IsReadOnly"/>
        public bool IsReadOnly
        {
            get { return _isReadOnly || _owner.IsReadOnly; }
        }

        /// <summary>
        /// If property (and mikrotik field) are madatory during load - should be present in resultset.
        /// </summary>
        /// <seealso cref="TikPropertyAttribute.IsMandatory"/>
        public bool IsMandatory { get; private set; }

        /// <summary>
        /// Defaukt value of the property.
        /// </summary>
        /// <seealso cref="TikPropertyAttribute.DefaultValue"/>
        public string? DefaultValue { get; private set; }

        /// <summary>
        /// If value should be unset during update (save modified entity) when property contains default value (set to default will be called when false).
        /// </summary>
        /// <seealso cref="TikPropertyAttribute.UnsetOnDefault"/>
        public bool UnsetOnDefault { get; private set; }

        /// <summary>
        /// If the field holds free-form text that can contain the CLI as-value format's own separators,
        /// and the entity must therefore be read as JSON on CLI transports.
        /// </summary>
        /// <seealso cref="TikPropertyAttribute.IsFreeText"/>
        public bool IsFreeText { get; private set; }

        /// <summary>
        /// If the field is a valueless presence flag, i.e. the binary API spells "set" as an empty value.
        /// </summary>
        /// <seealso cref="TikPropertyAttribute.IsPresenceFlag"/>
        public bool IsPresenceFlag { get; private set; }

        private PropertyInfo PropertyInfo { get; set; }

        private readonly Func<object, object?>? _getter;
        private readonly Action<object, object>? _setter;

        /// <summary>
        /// The wire-value ↔ member tables of <see cref="ValueType"/>, or null when the property is not an
        /// enum. Held per accessor rather than looked up per conversion — the shared cache behind
        /// <see cref="TikEnumMetadata.Get"/> is what makes it one table per enum type, this field is what
        /// makes reaching it free.
        /// </summary>
        private readonly TikEnumMetadata? _enumMetadata;

        private ITikTypeConverter? _converter;

        /// <summary>
        /// The <see cref="ITikTypeConverter"/> handling <see cref="ValueType"/>, or null when none does.
        /// </summary>
        /// <remarks>
        /// Resolved on the first conversion rather than in the ctor, and only for a type no built-in
        /// claimed — so a converter registered after the entity was first used still takes effect instead of
        /// being silently ignored, and a built-in type never pays for the lookup. The assignment is
        /// idempotent, so the missing lock costs at most a repeated scan.
        /// </remarks>
        private ITikTypeConverter? ResolveConverter()
        {
            return _converter ?? (_converter = TikTypeConverters.Resolve(ValueType));
        }

        /// <summary>
        /// True when this accessor reads and writes the property through a compiled delegate rather than
        /// through <see cref="PropertyInfo"/>. False means the platform refused to bind one and the accessor
        /// fell back to reflection — correct, but ~an order of magnitude slower per field per row.
        /// </summary>
        /// <remarks>
        /// Exposed for <c>CompiledAccessorTests</c>: the fallback is silent by design (a load must not fail
        /// because a delegate could not be bound), so without this the test suite could not tell whether the
        /// fast path is being taken at all.
        /// </remarks>
        internal bool UsesCompiledAccessors
        {
            get { return _getter != null && (_setter != null || PropertyInfo.SetMethod == null); }
        }

        /// <summary>
        /// .ctor
        /// </summary>
        /// <param name="owner">Metadata of the owning entity.</param>
        /// <param name="propertyInfo">PropertyInfo of the accessed  entity property.</param>
        public TikEntityPropertyAccessor(TikEntityMetadata owner, PropertyInfo propertyInfo)
        {
            _owner = owner;

            PropertyInfo = propertyInfo;
            _getter = BuildGetter(propertyInfo);
            _setter = BuildSetter(propertyInfo);

            //From property code
            PropertyName = propertyInfo.Name;
            PropertyType = propertyInfo.PropertyType;
            ValueType = Nullable.GetUnderlyingType(PropertyType) ?? PropertyType;
            IsNullable = ValueType != PropertyType;
            // Before DefaultValue below, which formats the CLR default through ConvertToString.
            if (ValueType.GetTypeInfo().IsEnum)
                _enumMetadata = TikEnumMetadata.Get(ValueType);

            //From TikPropertyAttribute attribute
            var propertyAttribute = propertyInfo.GetCustomAttribute<TikPropertyAttribute>(true);
            if (propertyAttribute == null)
                throw new ArgumentException("Property must be decorated by TikPropertyAttribute.", "propertyInfo");
            FieldName = propertyAttribute.FieldName;
            _isReadOnly =
                (propertyInfo.SetMethod == null)
                || (!propertyInfo.CanWrite) || (propertyAttribute.IsReadOnly);
            IsMandatory = propertyAttribute.IsMandatory;
            if (propertyAttribute.DefaultValue != null)
                DefaultValue = NormalizeDefaultValue(propertyAttribute.DefaultValue);
            else if (IsNullable)
                // A nullable property that declares no default HAS no default: its unset state is null, and
                // null is already "do not send". Computing one from the underlying type (which would give a
                // bool? the value "no") would put back exactly the conflation nullability removes.
                DefaultValue = null;
            else if (PropertyType.GetTypeInfo().IsValueType)
                DefaultValue = ConvertToString(Activator.CreateInstance(PropertyType)); //default value of value type. for example: (default)int
            else
                DefaultValue = "";
            UnsetOnDefault = propertyAttribute.UnsetOnDefault;
            IsFreeText = propertyAttribute.IsFreeText;
            IsPresenceFlag = propertyAttribute.IsPresenceFlag;
        }

        /// <summary>
        /// Readable description of the accessor.
        /// </summary>
        /// <returns>Readable description of the accessor.</returns>
        public override string ToString()
        {
            return PropertyName + "(" + FieldName + ")";
        }

        // ── Compiled accessors ─────────────────────────────────────────────────
        //
        // The mapper reads or writes EVERY mapped property of EVERY row, so PropertyInfo.GetValue/SetValue —
        // which re-resolves the accessor method and validates the argument on each call — is the mapper's
        // dominant cost on a bulk load. The delegate is bound once, here, and the metadata that owns this
        // accessor is itself cached per entity type (TikEntityMetadataCache), so the binding happens once per
        // property per process.
        //
        // Bound with MethodInfo.CreateDelegate rather than an expression tree, which is what the plan
        // suggested: a compiled lambda is emitted into an anonymous assembly and cannot call a NON-PUBLIC
        // accessor, and read-only entity properties are declared `{ get; private set; }` throughout — .id
        // among them, on every entity that has one. Reflection-based delegate creation has no such limit.
        //
        // Every failure falls back to PropertyInfo. Delegate binding needs a runtime that permits it, and a
        // load that threw on a platform which does not (AOT, a restricted host) would be a far worse outcome
        // than a slow one. UsesCompiledAccessors is how a test tells the two apart.

        private static Func<object, object?>? BuildGetter(PropertyInfo propertyInfo)
        {
            MethodInfo? getMethod = propertyInfo.GetMethod;
            if (getMethod == null || getMethod.IsStatic || propertyInfo.GetIndexParameters().Length > 0)
                return null;

            return (Func<object, object?>?)BindAccessor("MakeGetter", getMethod, propertyInfo);
        }

        private static Action<object, object>? BuildSetter(PropertyInfo propertyInfo)
        {
            MethodInfo? setMethod = propertyInfo.SetMethod;
            if (setMethod == null || setMethod.IsStatic || propertyInfo.GetIndexParameters().Length > 0)
                return null;

            return (Action<object, object>?)BindAccessor("MakeSetter", setMethod, propertyInfo);
        }

        private static object? BindAccessor(string factoryName, MethodInfo accessor, PropertyInfo propertyInfo)
        {
            try
            {
                return typeof(TikEntityPropertyAccessor).GetTypeInfo()
                    .GetDeclaredMethod(factoryName)! // factoryName always names one of this class's own static methods
                    .MakeGenericMethod(accessor.DeclaringType!, propertyInfo.PropertyType) // a real property's accessor always has a declaring type
                    .Invoke(null, new object[] { accessor });
            }
            catch (Exception)
            {
                // Includes the TargetInvocationException wrapping whatever CreateDelegate refused, and the
                // MakeGenericMethod failure for a value-type declaring type (whose accessor takes a ref
                // receiver and cannot be bound to Func<TEntity,TValue> at all).
                return null;
            }
        }

        private static Func<object, object?> MakeGetter<TEntity, TValue>(MethodInfo getMethod)
        {
            var typed = (Func<TEntity, TValue>)getMethod.CreateDelegate(typeof(Func<TEntity, TValue>));
            return entity => typed((TEntity)entity);
        }

        private static Action<object, object> MakeSetter<TEntity, TValue>(MethodInfo setMethod)
        {
            var typed = (Action<TEntity, TValue>)setMethod.CreateDelegate(typeof(Action<TEntity, TValue>));
            return (entity, value) => typed((TEntity)entity, (TValue)value);
        }

        private object? ConvertFromString(string? strValue)
        {
            try
            {
                // A nullable property is the only one that can carry "the router did not report this field"
                // as itself. Everything else has to fall through and be parsed, including the empty string —
                // a valueless presence flag reads back as false, and that is existing behaviour, not this.
                if (IsNullable && strValue == null)
                    return null;

                // Past this point strValue is null only for a non-nullable property, which is a caller
                // error the parse calls below already turn into a FormatException via the catch - the `!`
                // just types that pre-existing behaviour instead of changing it.
                string value = strValue!;

                //convert to property real type
                if (ValueType == typeof(string))
                    return value;
                else if (ValueType == typeof(TimeSpan))
                    return TikTimeHelper.FromTikTimeToTimeSpan(value);
                // A duration the router may also answer with a word (none / disabled / auto). Both of the
                // forms the router writes durations in parse to the same value here, which is the point:
                // the API says "10s" and the CLI says "00:00:10" for the same field.
                // Rates, like durations, arrive spelled differently per transport: 1000000 over the API,
                // 1M over the CLI. Both parse to the same value here.
                else if (ValueType == typeof(TikDataRate))
                    return value.Length == 0 ? (IsNullable ? (object?)null : default(TikDataRate)) : TikDataRate.Parse(value);
                else if (ValueType == typeof(TikRatePair))
                    return value.Length == 0 ? (IsNullable ? (object?)null : default(TikRatePair)) : TikRatePair.Parse(value);
                else if (ValueType == typeof(TikDuration))
                {
                    // An empty value is the router saying the field carries nothing, which is not the same
                    // as a zero-length duration. A nullable property can say that; a non-nullable one has
                    // nowhere to put it and keeps the type's own default.
                    if (value.Length == 0)
                        return IsNullable ? (object?)null : default(TikDuration);
                    return TikDuration.Parse(value);
                }
                // InvariantCulture on every numeric conversion, in both directions. The thread's culture
                // has no business here: the router's wire form is invariant, and the digits being the same
                // in every culture is not enough — a few (sv-SE, fi-FI) render minus as U+2212, which
                // RouterOS will not parse, and which will not parse the router's own U+002D back.
                else if (ValueType == typeof(int))
                    return int.Parse(value, CultureInfo.InvariantCulture);
                else if (ValueType == typeof(long))
                    return long.Parse(value, CultureInfo.InvariantCulture);
                else if (ValueType == typeof(byte))
                    return byte.Parse(value, CultureInfo.InvariantCulture);
                else if (ValueType == typeof(uint))
                    return uint.Parse(value, CultureInfo.InvariantCulture);
                else if (ValueType == typeof(ulong))
                    return ulong.Parse(value, CultureInfo.InvariantCulture);
                else if (ValueType == typeof(DateTime))
                    return TikDateTimeHelper.FromTikDateTime(value);
                else if (ValueType == typeof(MacAddress))
                    return new MacAddress(value);
                else if (ValueType == typeof(bool))
                {
                    // A presence flag is "set" by BEING THERE: the binary API and REST send `fib=` with an
                    // empty value and omit the word when it is clear, so the empty string is the router
                    // saying true. Absence is handled above (null for a nullable property) and is not
                    // turned into false here — that is the router reporting nothing, not reporting false.
                    if (IsPresenceFlag && value.Length == 0)
                        return true;
                    return string.Equals(value, "true", StringComparison.OrdinalIgnoreCase) || string.Equals(value, "yes", StringComparison.OrdinalIgnoreCase);
                }
                else if (ValueType.GetTypeInfo().IsEnum)
                {
                    // _enumMetadata is set in the constructor exactly when ValueType is an enum (see there).
                    if (_enumMetadata!.IsFlags && value.Contains(','))
                    {
                        long result = 0;
                        foreach (string part in value.Split(','))
                            result |= _enumMetadata.ParseNumeric(part.Trim());
                        return Enum.ToObject(ValueType, result);
                    }
                    else
                    {
                        return _enumMetadata.Parse(value);
                    }
                }
                else
                {
                    var converter = ResolveConverter();
                    if (converter != null)
                        return converter.ConvertFromString(value, ValueType);

                    throw new NotImplementedException(string.Format("Property type {0} not supported. Register an ITikTypeConverter for it via TikTypeConverters.Register.", ValueType));
                }
            }
            catch(NotImplementedException)
            {
                throw;
            }
            catch(Exception ex)
            {
                throw new FormatException(string.Format("Value '{0}' for property '{1}({2})' is not in expected format '{3}'.", strValue, PropertyName, FieldName, ValueType), ex);
            }
        }

        private string? ConvertToString(object? propValue)
        {
            // Null reaches here only from a nullable property that was never assigned. It has no wire form —
            // the point is that nothing is sent — so it stays null all the way out to the caller.
            if (propValue == null)
                return null;

            if (propValue is string)
                return (string)propValue;

            //convert to string used in mikrotik
            if (ValueType == typeof(string))
                return propValue.ToString();
            else if (ValueType == typeof(TimeSpan))
                return TikTimeHelper.ToTikTime((int)((TimeSpan)propValue).TotalSeconds);
            else if (ValueType == typeof(TikDuration))
                return ((TikDuration)propValue).ToString();
            else if (ValueType == typeof(TikDataRate))
                return ((TikDataRate)propValue).ToString();
            else if (ValueType == typeof(TikRatePair))
                return ((TikRatePair)propValue).ToString();
            else if (ValueType == typeof(int))
                return ((int)propValue).ToString(CultureInfo.InvariantCulture);
            else if (ValueType == typeof(long))
                return ((long)propValue).ToString(CultureInfo.InvariantCulture);
            // byte parses on the way in (above) and had no branch here, so writing one fell through to the
            // converter lookup and threw — and for a NON-nullable byte property it threw while the metadata
            // was still being built, because DefaultValue is formatted through this method.
            else if (ValueType == typeof(byte))
                return ((byte)propValue).ToString(CultureInfo.InvariantCulture);
            else if (ValueType == typeof(uint))
                return ((uint)propValue).ToString(CultureInfo.InvariantCulture);
            else if (ValueType == typeof(ulong))
                return ((ulong)propValue).ToString(CultureInfo.InvariantCulture);
            else if (ValueType == typeof(DateTime))
                return TikDateTimeHelper.ToTikDateTime((DateTime)propValue);
            else if (ValueType == typeof(MacAddress))
                return ((MacAddress)propValue).Address;
            else if (ValueType == typeof(bool))
                return ((bool)propValue) ? "yes" : "no"; //TODO add attribute definition for support true/false
            else if (ValueType.GetTypeInfo().IsEnum)
            {
                // _enumMetadata is set in the constructor exactly when ValueType is an enum (see there).
                if (_enumMetadata!.IsFlags)
                    return _enumMetadata.FormatFlags(propValue);
                else
                    return _enumMetadata.Format(propValue);
            }
            else
            {
                var converter = ResolveConverter();
                if (converter != null)
                    return converter.ConvertToString(propValue, ValueType);

                throw new NotImplementedException(string.Format("Property type {0} not supported. Register an ITikTypeConverter for it via TikTypeConverters.Register.", ValueType));
            }
        }

        /// <summary>
        /// Returns if accessed property of given <paramref name="entity"/> contains null or <see cref="DefaultValue"/>.
        /// </summary>
        /// <param name="entity"></param>
        /// <returns>True if accessed property od given entity contains default value.</returns>
        public bool HasDefaultValue(object entity)
        {
            string? propValue = GetEntityValue(entity);

            return (propValue == null) || (Convert.ToString(propValue) == DefaultValue);
        }

        /// <summary>
        /// Puts a declared <see cref="TikPropertyAttribute.DefaultValue"/> into the same spelling the
        /// property itself produces, so the two can be compared as strings.
        /// </summary>
        /// <remarks>
        /// It matters for the types that read more than one spelling — durations and rates — and there it
        /// matters a lot: the router writes
        /// the same duration as <c>10s</c> over the API and <c>00:00:10</c> over the CLI, and entity
        /// defaults in this repository are written in both spellings. Without this, a field sitting at its
        /// default reads as CHANGED on one transport and unchanged on the other — so <c>Save</c> would send
        /// a value the caller never set, on some transports only.
        /// <para>
        /// Anything that is not a normalizable type is left exactly as declared: this must not become a
        /// general rewriting pass over values whose spelling is already the one the router expects.
        /// </para>
        /// </remarks>
        private string NormalizeDefaultValue(string declared)
        {
            if (ValueType == typeof(TikDuration))
                return TikDuration.TryParse(declared, out TikDuration duration) ? duration.ToString() : declared;

            if (ValueType == typeof(TikDataRate))
                return TikDataRate.TryParse(declared, out TikDataRate rate) ? rate.ToString() : declared;

            if (ValueType == typeof(TikRatePair))
                return TikRatePair.TryParse(declared, out TikRatePair pair) ? pair.ToString() : declared;

            return declared;
        }

        /// <summary>
        /// Sets the value of accesed property on given <paramref name="entity"/>.
        /// </summary>
        /// <param name="entity">Entity to be modified.</param>
        /// <param name="propValue">New property value.</param>
        public void SetEntityValue(object entity, string? propValue)
        {
            object? value = ConvertFromString(propValue);

            if (_setter != null)
                _setter(entity, value!); //NOTE: works even if setter is private. `!`: the delegate's boxed
                                          //object parameter legitimately carries null for a nullable property;
                                          //this is unchanged runtime behaviour, just untyped for null here.
            else
                PropertyInfo.SetValue(entity, value);
        }

        /// <summary>
        /// Gets the value of accesed property from given <paramref name="entity"/>.
        /// </summary>
        /// <param name="entity">Entity to read peroperty value from.</param>
        /// <returns>Property value from giuven entity</returns>
        public string? GetEntityValue(object entity)
        {
            object? propValue = _getter != null ? _getter(entity) : PropertyInfo.GetValue(entity);

            // A null NULLABLE property means "nothing was said about this field", and that has to survive to
            // the caller as null so the save path can leave the field out. A null reference property keeps the
            // old substitution: a string has no unset state to distinguish, and turning its null into null
            // here would change what every existing entity sends.
            if (propValue == null && !IsNullable)
                propValue = DefaultValue;

            return ConvertToString(propValue);
        }
    }
}
