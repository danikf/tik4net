using System;
using System.Diagnostics.CodeAnalysis;
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
    [RequiresUnreferencedCode(TikTrimming.MapperMessage)]
    [RequiresDynamicCode(TikTrimming.DynamicCodeMessage)]
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
        /// Other names the router prints this field under (empty when there are none).
        /// </summary>
        /// <seealso cref="TikPropertyAttribute.AlternateNames"/>
        public IReadOnlyList<string> AlternateNames { get; private set; } = Array.Empty<string>();

        /// <summary>
        /// The name <paramref name="sentence"/> carries this field under — <see cref="FieldName"/> or, failing that,
        /// the first of <see cref="AlternateNames"/> it carries — or <c>null</c> when it carries none of them.
        /// </summary>
        internal string? NameInSentence(ITikReSentence sentence)
        {
            if (sentence.TryGetResponseField(FieldName, out _))
                return FieldName;
            foreach (string name in AlternateNames)
                if (sentence.TryGetResponseField(name, out _))
                    return name;
            return null;
        }

        /// <summary>
        /// The name to WRITE this field under for <paramref name="entity"/>: the name it was read under
        /// (see <see cref="TikPropertyAttribute.AlternateNames"/>), else <see cref="FieldName"/>.
        /// </summary>
        internal string WriteName(object entity)
            => AlternateNames.Count == 0 ? FieldName : TikEntityNotes.NamesRead.Get(entity, FieldName) ?? FieldName;

        /// <summary>
        /// If property (and mikrotik field) is R/O — either because the property says so, or because the
        /// menu offers neither <c>add</c> nor <c>set</c> and so has nothing to write with.
        /// </summary>
        /// <remarks>
        /// The entity-level half is <see cref="TikEntityMetadata.AreFieldsReadOnly"/>, not "the entity
        /// supports no verb at all": a menu that only offers <c>remove</c> (<c>/ppp/active</c>) still has
        /// read-only fields, and reading the verb set as a whole here would make them writable and start
        /// putting them on a <c>/set</c> the router does not have.
        /// </remarks>
        /// <seealso cref="TikPropertyAttribute.IsReadOnly"/>
        /// <seealso cref="TikEntityMetadata.AreFieldsReadOnly"/>
        public bool IsReadOnly
        {
            get { return _isReadOnly || _owner.AreFieldsReadOnly; }
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

        /// <summary>The field's WinBox label, when declared.</summary>
        /// <seealso cref="TikPropertyAttribute.WinboxLabel"/>
        public string? WinboxLabel { get; private set; }

        /// <summary>
        /// If the router changes the field's value by itself between two reads (a clock), so a full-update save
        /// compares it against what was loaded rather than against a fresh read.
        /// </summary>
        /// <seealso cref="TikPropertyAttribute.ChangesOnItsOwn"/>
        public bool ChangesOnItsOwn { get; private set; }

        /// <summary>
        /// If the field is a secret RouterOS 7 hides from a terminal print unless asked with <c>show-sensitive</c>.
        /// </summary>
        /// <seealso cref="TikPropertyAttribute.IsSensitive"/>
        public bool IsSensitive { get; private set; }

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
        private readonly TikWireConverter _wire;

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
            if (PropertyType.GetTypeInfo().IsGenericType && PropertyType.GetGenericTypeDefinition() == typeof(TikField<>))
            {
                // TikField<T>: the value type is T's, and the property is nullable by construction - a field the row
                // lacks is Absent, and "unset" is an assigned null.
                Type inner = PropertyType.GetTypeInfo().GenericTypeArguments[0];
                if (inner.GetTypeInfo().IsValueType && Nullable.GetUnderlyingType(inner) == null)
                    throw new ArgumentException(string.Format(
                        "{0}.{1}: TikField<{2}> must use the nullable form TikField<{2}?>, so that assigning null compiles.",
                        propertyInfo.DeclaringType?.Name, propertyInfo.Name, inner.Name), nameof(propertyInfo));
                IsWrapped = true;
                ValueType = Nullable.GetUnderlyingType(inner) ?? inner;
                IsNullable = true;
                var factories = typeof(TikEntityPropertyAccessor).GetTypeInfo().GetDeclaredMethod(nameof(MakeWrappers))!
                    .MakeGenericMethod(inner);
                var wrappers = ((Func<object?, bool, object>, Func<string, object>))factories.Invoke(null, null)!;
                _wrapPresent = wrappers.Item1;
                _wrapUnparsed = wrappers.Item2;
                _absent = Activator.CreateInstance(PropertyType)!;
            }
            else
            {
                ValueType = Nullable.GetUnderlyingType(PropertyType) ?? PropertyType;
                IsNullable = ValueType != PropertyType;
            }
            _wire = new TikWireConverter(ValueType, IsNullable, propertyInfo.Name, propertyInfo.GetCustomAttribute<TikPropertyAttribute>(true)?.FieldName ?? propertyInfo.Name);
            // Before DefaultValue below, which formats the CLR default through ConvertToString.
            if (ValueType.GetTypeInfo().IsEnum)
                _enumMetadata = TikEnumMetadata.Get(ValueType);
            if (IsWrapped && _enumMetadata != null && _enumMetadata.IsFlags)
                // A set of words is a list: each member its own item, a word the enum lacks one item of its own, and on
                // a field that takes it each member with its own '!'. A bitmask can hold none of the three.
                throw new ArgumentException(string.Format(
                    "{0}.{1}: a [Flags] enum cannot be a TikField<T>'s value; declare TikField<TikValueList<{2}>?> with a plain enum.",
                    propertyInfo.DeclaringType?.Name, propertyInfo.Name, ValueType.Name), nameof(propertyInfo));

            //From TikPropertyAttribute attribute
            var propertyAttribute = propertyInfo.GetCustomAttribute<TikPropertyAttribute>(true);
            if (propertyAttribute == null)
                throw new ArgumentException("Property must be decorated by TikPropertyAttribute.", "propertyInfo");
            FieldName = propertyAttribute.FieldName;
            if (propertyAttribute.AlternateNames != null && propertyAttribute.AlternateNames.Length > 0)
                AlternateNames = propertyAttribute.AlternateNames.ToArray();
            _isReadOnly =
                (propertyInfo.SetMethod == null)
                || (!propertyInfo.CanWrite) || (propertyAttribute.IsReadOnly);
            if (IsWrapped && (propertyAttribute.IsMandatory || propertyAttribute.UnsetOnDefault))
                // Both would act on Absent: IsMandatory fails the load of a row that lacks the field (another RouterOS
                // version), UnsetOnDefault unsets it (Absent "equals the default"). Refused rather than ignored, so an
                // entity converted from a plain property cannot carry either over silently.
                throw new ArgumentException(string.Format(
                    "{0}.{1}: a TikField<T> property cannot declare {2}. A field the row lacks reads Absent, and an unset is an assigned null.",
                    propertyInfo.DeclaringType?.Name, propertyInfo.Name,
                    propertyAttribute.IsMandatory ? "IsMandatory" : "UnsetOnDefault"), nameof(propertyInfo));
            IsMandatory = propertyAttribute.IsMandatory;
            if (IsWrapped)
                // A TikField<T> property has no runtime default: a field the row lacks is Absent, and what an add
                // sends is what the caller assigned. A declared DefaultValue documents the router's default only.
                DefaultValue = null;
            else if (propertyAttribute.DefaultValue != null)
                DefaultValue = NormalizeDefaultValue(propertyAttribute.DefaultValue);
            else if (IsNullable || IsNullableReference(propertyInfo))
                // A nullable property that declares no default HAS no default: its unset state is null, and
                // null is already "do not send". Computing one from the underlying type (which would give a
                // bool? the value "no") would put back exactly the conflation nullability removes.
                // The same for a `string?` (or any reference type annotated nullable): without this it read as
                // "" when the router printed no such field — a field another RouterOS version lacks, or one a
                // CLI print leaves out — and nothing could tell that apart from an empty value.
                DefaultValue = null;
            else if (PropertyType.GetTypeInfo().IsValueType)
                DefaultValue = ConvertToString(Activator.CreateInstance(PropertyType)); //default value of value type. for example: (default)int
            else
                DefaultValue = "";
            UnsetOnDefault = propertyAttribute.UnsetOnDefault;
            IsFreeText = propertyAttribute.IsFreeText;
            IsPresenceFlag = propertyAttribute.IsPresenceFlag;
            _wire.IsPresenceFlag = IsPresenceFlag;
            WinboxLabel = string.IsNullOrWhiteSpace(propertyAttribute.WinboxLabel) ? null : propertyAttribute.WinboxLabel;
            ChangesOnItsOwn = propertyAttribute.ChangesOnItsOwn;
            IsSensitive = propertyAttribute.IsSensitive;
            if (propertyAttribute.Negatable && !IsWrapped)
                // The flag lives on TikField<T>; a plain property has nowhere to keep it and would drop the '!' on a save.
                throw new ArgumentException(string.Format(
                    "{0}.{1}: Negatable needs a TikField<T> property, which carries the negation.",
                    propertyInfo.DeclaringType?.Name, propertyInfo.Name), nameof(propertyInfo));
            IsNegatable = propertyAttribute.Negatable;
            IsNegatableMembers = propertyAttribute.NegatableMembers;
            SetKeepsUnnamedHalf = propertyAttribute.SetKeepsUnnamedHalf;
            if (SetKeepsUnnamedHalf && !IsNegatableMembers)
                // The halves are the plain and the negated members; a field without the second has only one.
                throw new ArgumentException(string.Format(
                    "{0}.{1}: SetKeepsUnnamedHalf describes a list whose members are negated one by one; declare NegatableMembers.",
                    propertyInfo.DeclaringType?.Name, propertyInfo.Name), nameof(propertyInfo));

            if (IsWrapped && ValueType.GetTypeInfo().IsGenericType && ValueType.GetGenericTypeDefinition() == typeof(TikValueList<>))
            {
                // TikField<TikValueList<TItem>?>: the field holds several values, each read and written as a TItem would be.
                ItemType = ValueType.GetTypeInfo().GenericTypeArguments[0];
                if (Nullable.GetUnderlyingType(ItemType) != null)
                    throw new ArgumentException(string.Format(
                        "{0}.{1}: a list's items are never null; use TikValueList<{2}>, not TikValueList<{2}?>.",
                        propertyInfo.DeclaringType?.Name, propertyInfo.Name, Nullable.GetUnderlyingType(ItemType)!.Name), nameof(propertyInfo));
                var itemWire = new TikWireConverter(ItemType, false, PropertyName, FieldName);
                var codec = ((Func<string, object>, Func<object, string>))typeof(TikEntityPropertyAccessor).GetTypeInfo()
                    .GetDeclaredMethod(nameof(MakeListCodec))!.MakeGenericMethod(ItemType)
                    .Invoke(null, new object[] { itemWire, IsNegatableMembers, PropertyName, FieldName })!;
                _parseList = codec.Item1;
                _formatList = codec.Item2;
            }
            else if (IsNegatableMembers)
                // Only a list has members to negate; anywhere else the flag would be silently meaningless.
                throw new ArgumentException(string.Format(
                    "{0}.{1}: NegatableMembers needs a TikField<TikValueList<T>?> property, whose items carry their own '!'.",
                    propertyInfo.DeclaringType?.Name, propertyInfo.Name), nameof(propertyInfo));
        }

        /// <summary>
        /// Whether the field is a list whose members RouterOS negates one by one
        /// (<see cref="TikPropertyAttribute.NegatableMembers"/>): each item's <c>!</c> reads as its
        /// <see cref="TikValue{T}.IsNegated"/>.
        /// </summary>
        public bool IsNegatableMembers { get; private set; }

        /// <summary>
        /// Whether RouterOS's text <c>set</c> replaces only the half of this list it names
        /// (<see cref="TikPropertyAttribute.SetKeepsUnnamedHalf"/>).
        /// </summary>
        public bool SetKeepsUnnamedHalf { get; private set; }

        /// <summary>The item type of a <see cref="TikValueList{T}"/> property, else <c>null</c>.</summary>
        public Type? ItemType { get; private set; }

        private readonly Func<string, object>? _parseList;
        private readonly Func<object, string>? _formatList;

        private static (Func<string, object>, Func<object, string>) MakeListCodec<TItem>(
            TikWireConverter item, bool negatableMembers, string propertyName, string fieldName)
            => (text => TikValueListWire.Parse<TItem>(text, negatableMembers, item),
                list => TikValueListWire.Format((TikValueList<TItem>)list, negatableMembers, item, propertyName, fieldName));

        /// <summary>
        /// Whether the field is a matcher RouterOS negates with a leading <c>!</c> (<see cref="TikPropertyAttribute.Negatable"/>):
        /// the <c>!</c> reads as <see cref="TikField{T}.IsNegated"/> rather than as part of the value.
        /// </summary>
        public bool IsNegatable { get; private set; }

        /// <summary>
        /// Whether a reference-typed property is declared nullable (<c>string?</c>) under nullable reference types.
        /// </summary>
        /// <remarks>
        /// Read from the compiler's own metadata, since <c>NullabilityInfoContext</c> is not available on
        /// netstandard2.0: the property's <c>NullableAttribute</c> (first byte = the top-level annotation), else the
        /// <c>NullableContextAttribute</c> of the declaring type or an enclosing one. 2 means nullable. A property
        /// compiled without nullable annotations carries neither and is not nullable here — it keeps reading
        /// <c>""</c> for a field the row lacks, as before.
        /// </remarks>
        private static bool IsNullableReference(PropertyInfo property)
        {
            if (property.PropertyType.GetTypeInfo().IsValueType)
                return false;

            byte? flag = NullableFlag(property.CustomAttributes, "System.Runtime.CompilerServices.NullableAttribute");
            for (Type? t = property.DeclaringType; flag == null && t != null; t = t.DeclaringType)
                flag = NullableFlag(t.GetTypeInfo().CustomAttributes, "System.Runtime.CompilerServices.NullableContextAttribute");
            return flag == 2;
        }

        private static byte? NullableFlag(IEnumerable<CustomAttributeData> attributes, string attributeName)
        {
            foreach (var a in attributes)
            {
                if (a.AttributeType.FullName != attributeName || a.ConstructorArguments.Count != 1)
                    continue;
                object? arg = a.ConstructorArguments[0].Value;
                if (arg is byte b)
                    return b;
                if (arg is IReadOnlyCollection<CustomAttributeTypedArgument> list && list.Count > 0
                    && list.First().Value is byte first)
                    return first;
            }
            return null;
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

        /// <summary>
        /// True when the property is a <see cref="TikField{T}"/>: it carries whether the field was printed and whether
        /// it could be read, and <see cref="ValueType"/> is its <c>T</c>'s.
        /// </summary>
        public bool IsWrapped { get; private set; }

        private readonly Func<object?, bool, object>? _wrapPresent;
        private readonly Func<string, object>? _wrapUnparsed;
        private readonly object? _absent;

        private static (Func<object?, bool, object>, Func<string, object>) MakeWrappers<T>()
            => ((value, negated) => TikField<T>.FromPresent((T)value!, negated), raw => TikField<T>.FromUnparsed(raw));


        /// <summary>
        /// Copies this property from <paramref name="source"/> to <paramref name="target"/> as it is - for a
        /// <see cref="TikField{T}"/> its state too, so an assigned <c>null</c> stays an intent to unset rather than
        /// becoming Absent on the way through its string form.
        /// </summary>
        internal void CopyEntityValue(object source, object target)
        {
            if (IsWrapped)
                SetRaw(target, _getter != null ? _getter(source) : PropertyInfo.GetValue(source));
            else
                SetEntityValue(target, GetEntityValue(source));
        }

        /// <summary>
        /// The wire form of a boxed <see cref="TikField{T}"/> of this property: <c>null</c> for Absent (it has none), the
        /// router's own word for Unparsed, the formatted value — with a <c>[Flags]</c> value's unknown words — for Present.
        /// </summary>
        internal string? FormatWrapped(object boxedTikField)
        {
            var wrapped = (ITikField)boxedTikField;
            if (wrapped.State == TikFieldState.Absent)
                return null;
            if (wrapped.State == TikFieldState.Unparsed)
                return wrapped.RawValue;
            if (wrapped.BoxedValue == null)
                return null;
            if (wrapped.IsNegated && !IsNegatable)
                // Dropping the '!' would write the opposite matcher; writing it to a field that takes none is refused by
                // the router on some transports and read as a literal '!' on others.
                throw new InvalidOperationException(string.Format(
                    "Property '{0}({1})' holds a negated value, and the field is not marked Negatable.", PropertyName, FieldName));
            if (_formatList != null)
            {
                string items = _formatList(wrapped.BoxedValue);
                if (!wrapped.IsNegated)
                    return items;
                if (items.Length == 0)
                    // RouterOS refuses a bare '!' (tcp-flags=! → "ambiguous value of flag"): there is nothing to negate.
                    throw new InvalidOperationException(string.Format(
                        "Property '{0}({1})' holds a negated empty list, which RouterOS cannot be sent.", PropertyName, FieldName));
                // A list whose members carry their own '!' spells the whole-list one as a bare leading element (!,syn,!ack);
                // any other list as a '!' in front of the first item (!22,8291).
                return (IsNegatableMembers ? "!," : "!") + items;
            }
            string? text = ConvertToString(wrapped.BoxedValue);
            return wrapped.IsNegated ? "!" + text : text;
        }

        /// <summary>The <see cref="TikField{T}"/> itself, boxed — only for a wrapped property.</summary>
        internal object GetWrapped(object entity)
            => (_getter != null ? _getter(entity) : PropertyInfo.GetValue(entity))!;

        private void SetRaw(object entity, object? value)
        {
            if (_setter != null)
                _setter(entity, value!);
            else
                PropertyInfo.SetValue(entity, value);
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

        private object? ConvertFromString(string? strValue) => ConvertFromString(strValue, out _);

        // unknownWord: the router's word(s) an enum property read as its TikEnumUnknown member for, else null.
        private object? ConvertFromString(string? strValue, out string? unknownWord)
            => _wire.ConvertFromString(strValue, out unknownWord);

        private string? ConvertToString(object? propValue) => _wire.ConvertToString(propValue);

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

            if (ValueType == typeof(TikHexNumber))
                return TikHexNumber.TryParse(declared, out TikHexNumber hex) ? hex.ToString() : declared;

            return declared;
        }

        /// <summary>
        /// Sets the value of accesed property on given <paramref name="entity"/>.
        /// </summary>
        /// <param name="entity">Entity to be modified.</param>
        /// <param name="propValue">New property value.</param>
        public void SetEntityValue(object entity, string? propValue)
        {
            if (IsWrapped)
            {
                SetRaw(entity, Wrap(entity, propValue));
                return;
            }

            object? value = ConvertFromString(propValue, out string? unknownWord);
            if (_enumMetadata?.UnknownMember != null)
            {
                if (unknownWord != null)
                    TikEntityNotes.UnknownWords.Record(entity, FieldName, unknownWord);
                else
                    TikEntityNotes.UnknownWords.Forget(entity, FieldName);
            }

            if (_setter != null)
                _setter(entity, value!); //NOTE: works even if setter is private. `!`: the delegate's boxed
                                          //object parameter legitimately carries null for a nullable property;
                                          //this is unchanged runtime behaviour, just untyped for null here.
            else
                PropertyInfo.SetValue(entity, value);
        }

        private static bool IsBoolWord(string value)
            => string.Equals(value, "true", StringComparison.OrdinalIgnoreCase) || string.Equals(value, "false", StringComparison.OrdinalIgnoreCase)
            || string.Equals(value, "yes", StringComparison.OrdinalIgnoreCase) || string.Equals(value, "no", StringComparison.OrdinalIgnoreCase);

        // The TikField<T> read: null is Absent; a value the type cannot hold - a format error, or a word a plain enum
        // does not know - is Unparsed with the router's word; everything else is Present. A [Flags] enum with an
        // Unknown member keeps its known parts and remembers the unknown words, as a plain property does, so a
        // save appends them.
        private object Wrap(object entity, string? propValue)
        {
            if (propValue == null)
                return _absent!;

            if (_parseList != null)
            {
                // The whole-list '!': a bare leading element on a list whose members carry their own (!,syn,!ack), else a
                // '!' in front of the first item (!22,8291). Never Unparsed: an item the type cannot hold stays a word.
                bool wholeNegated = false;
                string items = propValue;
                if (IsNegatable && IsNegatableMembers && (items == "!" || items.StartsWith("!,", StringComparison.Ordinal)))
                {
                    wholeNegated = true;
                    items = items.Length > 1 ? items.Substring(2) : "";
                }
                else if (IsNegatable && !IsNegatableMembers && items.Length > 1 && items[0] == '!')
                {
                    wholeNegated = true;
                    items = items.Substring(1);
                }
                return _wrapPresent!(_parseList(items), wholeNegated);
            }

            // A negatable matcher's leading '!' is the negation, not the value's first character; the rest parses as T. A
            // value that then does not parse stays Unparsed with the whole word, '!' included, so a save writes back
            // exactly what the router printed.
            string word = propValue;
            bool negated = IsNegatable && propValue.Length > 1 && propValue[0] == '!';
            if (negated)
                propValue = propValue.Substring(1);

            // A plain bool reads every word but true/yes as false; a TikField<bool?> knows the four the router
            // prints (and a presence flag's empty value), and anything else is a value it cannot hold.
            if (ValueType == typeof(bool) && !(IsPresenceFlag && propValue.Length == 0) && !IsBoolWord(propValue))
                return _wrapUnparsed!(word);

            object? value;
            string? unknownWord;
            try
            {
                value = ConvertFromString(propValue, out unknownWord);
            }
            catch (FormatException)
            {
                return _wrapUnparsed!(word);
            }

            if (_enumMetadata != null && unknownWord != null)
                return _wrapUnparsed!(word);
            return _wrapPresent!(value, negated);
        }

        /// <summary>
        /// Gets the value of accesed property from given <paramref name="entity"/>.
        /// </summary>
        /// <param name="entity">Entity to read peroperty value from.</param>
        /// <returns>Property value from giuven entity</returns>
        public string? GetEntityValue(object entity)
        {
            object? propValue = _getter != null ? _getter(entity) : PropertyInfo.GetValue(entity);

            if (IsWrapped)
                return FormatWrapped(propValue!);

            // A null NULLABLE property means "nothing was said about this field", and that has to survive to
            // the caller as null so the save path can leave the field out. A null reference property keeps the
            // old substitution: a string has no unset state to distinguish, and turning its null into null
            // here would change what every existing entity sends.
            if (propValue == null && !IsNullable)
                propValue = DefaultValue;

            if (propValue != null && _enumMetadata?.UnknownMember != null)
            {
                long numeric = Convert.ToInt64(propValue);
                bool unknown = _enumMetadata.IsFlags
                    ? (numeric & _enumMetadata.UnknownNumeric) == _enumMetadata.UnknownNumeric
                    : numeric == _enumMetadata.UnknownNumeric;
                if (unknown)
                    return UnknownToString(entity, numeric);
            }

            return ConvertToString(propValue);
        }

        // An Unknown member stands for a word the router printed and this enum does not know. Writing is strict:
        // the word read is written back unchanged, and an Unknown the caller assigned — with no word behind it —
        // has nothing RouterOS could be sent, so it throws rather than inventing one.
        private string UnknownToString(object entity, long numeric)
        {
            string? word = TikEntityNotes.UnknownWords.Get(entity, FieldName);
            if (word == null)
                throw new FormatException(string.Format(
                    "Property '{0}({1})' holds {2}.{3}, which stands for a word read from the router — and this entity "
                    + "was not read with one. Assign a member RouterOS accepts.",
                    PropertyName, FieldName, ValueType.Name, Enum.ToObject(ValueType, _enumMetadata!.UnknownNumeric)));
            if (!_enumMetadata!.IsFlags)
                return word;
            string known = _enumMetadata.FormatFlags(Enum.ToObject(ValueType, numeric & ~_enumMetadata.UnknownNumeric));
            return known.Length == 0 ? word : known + "," + word;
        }
    }
}
