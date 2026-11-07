using System;
using System.Diagnostics.CodeAnalysis;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;

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
        /// The type actually converted to and from the wire: the <c>T</c> of a <see cref="TikField{T}"/> or a
        /// <see cref="TikNegatableField{T}"/>, without its nullable form; <see cref="string"/> for a plain <c>.id</c>.
        /// </summary>
        public Type ValueType { get; private set; }

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
        internal TikEntityPropertyAccessor(TikEntityMetadata owner, PropertyInfo propertyInfo)
        {
            var attribute = propertyInfo.GetCustomAttribute<TikPropertyAttribute>(true)
                ?? throw new ArgumentException("Property must be decorated by TikPropertyAttribute.", nameof(propertyInfo));
            _owner = owner;

            PropertyInfo = propertyInfo;
            _getter = BuildGetter(propertyInfo);
            _setter = BuildSetter(propertyInfo);

            PropertyName = propertyInfo.Name;
            PropertyType = propertyInfo.PropertyType;
            FieldName = attribute.FieldName;
            if (attribute.AlternateNames != null && attribute.AlternateNames.Length > 0)
                AlternateNames = attribute.AlternateNames.ToArray();
            IsMandatory = attribute.IsMandatory;
            IsFreeText = attribute.IsFreeText;
            IsPresenceFlag = attribute.IsPresenceFlag;
            WinboxLabel = string.IsNullOrWhiteSpace(attribute.WinboxLabel) ? null : attribute.WinboxLabel;
            ChangesOnItsOwn = attribute.ChangesOnItsOwn;
            IsSensitive = attribute.IsSensitive;
            SetKeepsUnnamedHalf = attribute.SetKeepsUnnamedHalf;
            SetKeepsUnnamedMembers = attribute.SetKeepsUnnamedMembers;
            RouterSortsMembers = attribute.RouterSortsMembers;

            Type? fieldKind = PropertyType.GetTypeInfo().IsGenericType ? PropertyType.GetGenericTypeDefinition() : null;
            IsWrapped = fieldKind == typeof(TikField<>) || fieldKind == typeof(TikNegatableField<>);
            IsNegatable = fieldKind == typeof(TikNegatableField<>);
            Type declared = IsWrapped ? PropertyType.GetTypeInfo().GenericTypeArguments[0] : PropertyType;
            ValueType = Nullable.GetUnderlyingType(declared) ?? declared;
            if (ValueType.GetTypeInfo().IsGenericType && ValueType.GetGenericTypeDefinition() == typeof(TikValueList<>))
            {
                ItemType = ValueType.GetTypeInfo().GenericTypeArguments[0];
                IsNegatableMembers = TikNegatableValue.IsNegatableValue(ItemType);
                _isMemberSet = (IsNegatableMembers ? ItemType.GetTypeInfo().GenericTypeArguments[0] : ItemType).GetTypeInfo().IsEnum;
            }

            ValidateDeclaration(propertyInfo, attribute, declared);

            // The plain .id is the router's key: never a field a write sends.
            _isReadOnly = !IsWrapped || propertyInfo.SetMethod == null || !propertyInfo.CanWrite || attribute.IsReadOnly;
            _wire = new TikWireConverter(ValueType, PropertyName, FieldName, IsPresenceFlag);
            if (IsWrapped)
            {
                var wrappers = ((Func<object?, bool, object>, Func<string, object>))typeof(TikEntityPropertyAccessor).GetTypeInfo()
                    .GetDeclaredMethod(IsNegatable ? nameof(MakeNegatableWrappers) : nameof(MakeWrappers))!
                    .MakeGenericMethod(declared).Invoke(null, null)!;
                _wrapPresent = wrappers.Item1;
                _wrapUnparsed = wrappers.Item2;
                _absent = Activator.CreateInstance(PropertyType)!;
            }
            if (ItemType != null)
            {
                var codec = ((Func<string, object>, Func<object, string>))typeof(TikEntityPropertyAccessor).GetTypeInfo()
                    .GetDeclaredMethod(nameof(MakeListCodec))!.MakeGenericMethod(ItemType)
                    .Invoke(null, new object[] { new TikWireConverter(ItemType, PropertyName, FieldName) })!;
                _parseList = codec.Item1;
                _formatList = codec.Item2;
                if (RouterSortsMembers)
                    _sameMembers = (Func<object, object, bool>)typeof(TikEntityPropertyAccessor).GetTypeInfo()
                        .GetDeclaredMethod(nameof(MakeDistinctMembersComparer))!.MakeGenericMethod(ItemType).Invoke(null, null)!;
                else if (_isMemberSet)
                    _sameMembers = (x, y) => x.Equals(y);
            }
        }

        // Every declaration the mapper cannot honour is refused here, naming what to write instead, so a mistake
        // surfaces when the entity is first used rather than as wrong data on the router.
        private void ValidateDeclaration(PropertyInfo property, TikPropertyAttribute attribute, Type declared)
        {
            string owner = property.DeclaringType?.Name + "." + property.Name + ": ";
            if (!IsWrapped)
            {
                // The one plain property: the router's key, which always has a value on a loaded row and nothing to
                // say about one the row lacks.
                if (FieldName != TikSpecialProperties.Id || PropertyType != typeof(string))
                    throw new ArgumentException(owner + "a mapped property is a TikField<T?> (or a TikNegatableField<T?> for a field "
                        + "RouterOS negates) - TikField<" + PropertyType.Name + "?>; only the .id is a plain string.", nameof(property));
                return;
            }

            string kind = IsNegatable ? "TikNegatableField" : "TikField";
            if (declared.GetTypeInfo().IsValueType && Nullable.GetUnderlyingType(declared) == null)
                throw new ArgumentException(string.Format(CultureInfo.InvariantCulture, "{0}{2}<{1}> must use the nullable form {2}<{1}?>, so that assigning null compiles.",
                    owner, declared.Name, kind), nameof(property));
            if (attribute.IsMandatory)
                // A load must not fail because a row lacks a field (another RouterOS version): it reads Absent.
                throw new ArgumentException(owner + "a TikField<T> property cannot declare IsMandatory. A field the row lacks reads Absent.", nameof(property));
            if (ValueType == typeof(TimeSpan))
                // A duration field may hold a word (none, disabled, never), which a TimeSpan cannot.
                throw new ArgumentException(owner + "a TimeSpan cannot hold the words a duration field may print; declare " + kind + "<TikDuration?>.", nameof(property));
            if (TikNegatableValue.IsNegatableValue(ValueType))
                // A member's '!' belongs to a list item; a field negated as a whole is a TikNegatableField<T>.
                throw new ArgumentException(owner + "a TikNegatableValue<T> is a list member; declare TikField<TikValueList<TikNegatableValue<T>>?>, or a "
                    + "TikNegatableField<T> for a field negated as a whole.", nameof(property));
            RefuseFlags(owner, ValueType, property);
            if (ItemType != null)
            {
                if (Nullable.GetUnderlyingType(ItemType) != null)
                    throw new ArgumentException(string.Format(CultureInfo.InvariantCulture, "{0}a list's items are never null; use TikValueList<{1}>, not TikValueList<{1}?>.",
                        owner, Nullable.GetUnderlyingType(ItemType)!.Name), nameof(property));
                RefuseFlags(owner, IsNegatableMembers ? ItemType.GetTypeInfo().GenericTypeArguments[0] : ItemType, property);
            }
            if (SetKeepsUnnamedHalf && !IsNegatableMembers)
                // The halves are the plain and the negated members; a field without the second has only one.
                throw new ArgumentException(owner + "SetKeepsUnnamedHalf describes a list whose members are negated one by one; declare its items TikNegatableValue<T>.", nameof(property));
            if (SetKeepsUnnamedMembers && !IsNegatableMembers)
                // The members left out are sent denied, which takes a '!' on each.
                throw new ArgumentException(owner + "SetKeepsUnnamedMembers describes a list whose members are negated one by one; declare its items TikNegatableValue<T>.", nameof(property));
            if (SetKeepsUnnamedMembers && SetKeepsUnnamedHalf)
                throw new ArgumentException(owner + "SetKeepsUnnamedMembers and SetKeepsUnnamedHalf describe two different set behaviours; a field has one.", nameof(property));
            if (RouterSortsMembers && ItemType == null)
                // Only a list has members to sort; a plain string holding one compares by its text.
                throw new ArgumentException(owner + "RouterSortsMembers describes a list; declare TikField<TikValueList<T>?>.", nameof(property));
        }

        // A set of words is a list: each member its own item, a word the enum lacks one item of its own, and on a field
        // that takes it each member with its own '!'. A bitmask can hold none of the three.
        private static void RefuseFlags(string owner, Type type, PropertyInfo property)
        {
            if (type.GetTypeInfo().IsEnum && type.GetTypeInfo().GetCustomAttribute<FlagsAttribute>() != null)
                throw new ArgumentException(string.Format(CultureInfo.InvariantCulture, "{0}a [Flags] enum cannot be a field's value; declare TikField<TikValueList<{1}>?> with a plain enum.",
                    owner, type.Name), nameof(property));
        }

        /// <summary>
        /// Whether the field is a list whose members RouterOS negates one by one — a list of
        /// <see cref="TikNegatableValue{T}"/>: each item's <c>!</c> reads as its <see cref="TikNegatableValue{T}.IsNegated"/>.
        /// </summary>
        public bool IsNegatableMembers { get; private set; }

        /// <summary>
        /// Whether RouterOS's text <c>set</c> replaces only the half of this list it names
        /// (<see cref="TikPropertyAttribute.SetKeepsUnnamedHalf"/>).
        /// </summary>
        public bool SetKeepsUnnamedHalf { get; private set; }

        /// <summary>
        /// Whether RouterOS's text <c>set</c> keeps the members of this list it does not name, so an update sends the
        /// left-out ones denied (<see cref="TikPropertyAttribute.SetKeepsUnnamedMembers"/>).
        /// </summary>
        public bool SetKeepsUnnamedMembers { get; private set; }

        /// <summary>
        /// Whether RouterOS keeps this list as a set and prints it sorted, so a list merge compares it by its distinct
        /// members (<see cref="TikPropertyAttribute.RouterSortsMembers"/>).
        /// </summary>
        public bool RouterSortsMembers { get; private set; }

        /// <summary>The item type of a <see cref="TikValueList{T}"/> property, else <c>null</c>.</summary>
        public Type? ItemType { get; private set; }

        private readonly Func<string, object>? _parseList;
        private readonly Func<object, string>? _formatList;

        // A list of an enum's members. RouterOS keeps these as a bitmask and prints its own order, so their order says nothing.
        private readonly bool _isMemberSet;

        // How a list merge compares two present values whose text differs: by members, for a list whose order the router
        // sets (an enum's members, or a RouterSortsMembers list); null for a list whose order is configuration.
        private readonly Func<object, object, bool>? _sameMembers;

        // RouterOS drops a duplicate member of a sorted list, so the members are compared as a set, not a multiset.
        private static Func<object, object, bool> MakeDistinctMembersComparer<TItem>()
            => (a, b) => new HashSet<TikValue<TItem>>((TikValueList<TItem>)a).SetEquals((TikValueList<TItem>)b);

        private static (Func<string, object>, Func<object, string>) MakeListCodec<TItem>(TikWireConverter item)
            => (text => TikValueListWire.Parse<TItem>(text, item),
                list => TikValueListWire.Format((TikValueList<TItem>)list, item));

        /// <summary>
        /// Whether the field is a matcher RouterOS negates with a leading <c>!</c> — a <see cref="TikNegatableField{T}"/>
        /// property: the <c>!</c> reads as <see cref="TikNegatableField{T}.IsNegated"/> rather than as part of the value.
        /// </summary>
        public bool IsNegatable { get; private set; }

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
        /// True when the property is a <see cref="TikField{T}"/> or a <see cref="TikNegatableField{T}"/>: it carries whether the field was printed and whether
        /// it could be read, and <see cref="ValueType"/> is its <c>T</c>'s. False only for a plain <c>.id</c>.
        /// </summary>
        public bool IsWrapped { get; private set; }

        private readonly Func<object?, bool, object>? _wrapPresent;
        private readonly Func<string, object>? _wrapUnparsed;
        private readonly object? _absent;

        // Wrap passes negated=true only for a TikNegatableField<T> property; a TikField<T> has no negation to carry.
        private static (Func<object?, bool, object>, Func<string, object>) MakeWrappers<T>()
            => ((value, _) => (TikField<T>)(T)value!, raw => TikField<T>.FromWire(raw));

        private static (Func<object?, bool, object>, Func<string, object>) MakeNegatableWrappers<T>()
            => ((value, negated) => TikNegatableField<T>.FromPresent((T)value!, negated), raw => TikNegatableField<T>.FromWire(raw));


        /// <summary>
        /// Copies this property from <paramref name="source"/> to <paramref name="target"/> as it is - for a
        /// <see cref="TikField{T}"/> its state too, so an assigned <c>null</c> stays an intent to unset rather than
        /// becoming Absent on the way through its string form.
        /// </summary>
        internal void CopyEntityValue(object source, object target) => SetRaw(target, GetRaw(source));

        /// <summary>
        /// The wire form of a boxed <see cref="TikField{T}"/> of this property: <c>null</c> for Absent (it has none), the
        /// router's own word for Unparsed, the formatted value for Present.
        /// </summary>
        private string? FormatWrapped(object boxedTikField)
        {
            var wrapped = (ITikField)boxedTikField;
            if (wrapped.State == TikFieldState.Absent)
                return null;
            if (wrapped.State == TikFieldState.Unparsed)
                return wrapped.RawValue;
            if (wrapped.BoxedValue == null)
                return null;
            if (_formatList != null)
            {
                string items = _formatList(wrapped.BoxedValue);
                if (!wrapped.IsNegated)
                    return items;
                if (items.Length == 0)
                    // RouterOS refuses a bare '!' (tcp-flags=! → "ambiguous value of flag"): there is nothing to negate.
                    throw new InvalidOperationException(string.Format(CultureInfo.InvariantCulture, 
                        "Property '{0}({1})' holds a negated empty list, which RouterOS cannot be sent.", PropertyName, FieldName));
                return TikNegation.WholeListPrefix(IsNegatableMembers) + items;
            }
            string? text = _wire.ConvertToString(wrapped.BoxedValue);
            return wrapped.IsNegated ? TikNegation.Bang + text : text;
        }

        /// <summary>
        /// True when this is a wrapped property and its field is <see cref="TikFieldState.Absent"/> on <paramref name="entity"/>:
        /// nothing was said about it, which a write leaves out. Its wire form is <c>null</c>, as an assigned <c>null</c>'s is.
        /// </summary>
        internal bool IsAbsent(object entity)
            => IsWrapped && ((ITikField)GetWrapped(entity)).State == TikFieldState.Absent;

        /// <summary>
        /// Whether a list merge leaves this property alone, given what the property holds now and the value it would get
        /// (both as <see cref="GetRaw"/> returns them): they send the same form, or they are a list of an enum's members
        /// holding the same members, or a <see cref="TikPropertyAttribute.RouterSortsMembers"/> list holding the same
        /// distinct members. Any other list compares in order, because that order is configuration: the first
        /// DNS server a DHCP client gets is its primary. "No value" — Absent, or an assigned <c>null</c> — is one state here.
        /// </summary>
        internal bool MergeEquals(object? current, object? merged)
        {
            if (!IsWrapped)
                return (string?)current == (string?)merged;
            if (FormatWrapped(current!) == FormatWrapped(merged!))
                return true;
            var a = (ITikField)current!;
            var b = (ITikField)merged!;
            return _sameMembers != null && a.State == TikFieldState.Present && b.State == TikFieldState.Present
                && a.IsNegated == b.IsNegated && a.BoxedValue != null && b.BoxedValue != null && _sameMembers(a.BoxedValue, b.BoxedValue);
        }

        /// <summary>An assigned <c>null</c> of this wrapped property, boxed: the intent to unset.</summary>
        internal object AssignedNull() => _wrapPresent!(null, false);

        /// <summary>The <see cref="TikField{T}"/> itself, boxed — only for a wrapped property.</summary>
        internal object GetWrapped(object entity) => GetRaw(entity)!;

        /// <summary>The property's value as it is: a boxed <see cref="TikField{T}"/>, or the plain <c>.id</c>.</summary>
        internal object? GetRaw(object entity)
            => _getter != null ? _getter(entity) : PropertyInfo.GetValue(entity);

        /// <summary>Sets the property's value as it is, the counterpart of <see cref="GetRaw"/>.</summary>
        internal void SetRaw(object entity, object? value)
        {
            if (_setter != null)
                _setter(entity, value!);    // works even if the setter is private; a plain .id takes a null
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

        /// <summary>
        /// Sets the value of accesed property on given <paramref name="entity"/>.
        /// </summary>
        /// <param name="entity">Entity to be modified.</param>
        /// <param name="propValue">New property value.</param>
        public void SetEntityValue(object entity, string? propValue)
            => SetRaw(entity, IsWrapped ? Wrap(propValue) : propValue);

        // The TikField<T> read: null is Absent, and so is an empty word on a type with no empty value; a value the type
        // cannot hold - a format error, or a word an enum does not know - is Unparsed with the router's word; everything
        // else is Present.
        private object Wrap(string? word)
        {
            if (word == null)
                return _absent!;

            if (_parseList != null)
                return WrapList(word);

            // A negatable matcher's leading '!' is the negation, not the value's first character; the rest parses as T. A
            // value that then does not parse stays Unparsed with the whole word, '!' included, so a save writes back
            // exactly what the router printed.
            string rest = word;
            bool negated = IsNegatable && TikNegation.TrySplit(word, out rest);
            object? value;
            try
            {
                value = _wire.ConvertFromString(rest);
            }
            catch (FormatException)
            {
                return _wrapUnparsed!(word);
            }
            return value == null ? _absent! : _wrapPresent!(value, negated);
        }

        // An item the type cannot hold stays a word inside the list. A whole-list '!' with nothing behind it is no list at
        // all, and a negated empty list cannot be written either, so it stays the router's word.
        private object WrapList(string word)
        {
            string items = word;
            bool negated = IsNegatable && TikNegation.SplitWholeList(word, IsNegatableMembers, out items);
            if (negated && TikNegation.NegatesNothing(items))
                return _wrapUnparsed!(word);
            return _wrapPresent!(_parseList!(items), negated);
        }

        /// <summary>
        /// Gets the value of accesed property from given <paramref name="entity"/>.
        /// </summary>
        /// <param name="entity">Entity to read peroperty value from.</param>
        /// <returns>Property value from giuven entity</returns>
        public string? GetEntityValue(object entity)
        {
            object? value = GetRaw(entity);
            return IsWrapped ? FormatWrapped(value!) : (string?)value;
        }
    }
}
