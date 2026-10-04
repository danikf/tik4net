using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;

namespace tik4net.Objects
{
    /// <summary>What <see cref="TikFieldStrictnessExtensions.EnsureStrict{TEntity}(TEntity, TikStrictness, string[])"/> refuses.</summary>
    [Flags]
    public enum TikStrictness
    {
        /// <summary>A value the property's type cannot hold (<see cref="TikFieldState.Unparsed"/>) — the default.</summary>
        Unparsed = 1,

        /// <summary>
        /// A field the row did not carry (<see cref="TikFieldState.Absent"/>). Many fields are absent by design — per row
        /// type, per state — so this is for the fields a caller relies on; name the others in <c>allowAbsent</c>.
        /// </summary>
        Absent = 2,

        /// <summary>
        /// A <see cref="TikValueList{T}"/> with an item the type cannot hold — a member a newer RouterOS added
        /// (<see cref="TikValue{T}.IsWord"/>). The rest of the list is typed; this is for code that acts on every member.
        /// </summary>
        UnknownListItems = 4,
    }

    /// <summary>One <see cref="TikField{T}"/> property of an entity, as <see cref="TikFieldStrictnessExtensions.GetValueReport{TEntity}"/> sees it.</summary>
    public sealed class TikFieldReportItem
    {
        internal TikFieldReportItem(string propertyName, string fieldName, TikFieldState state, string? rawValue, string? unknownItems)
        {
            PropertyName = propertyName;
            FieldName = fieldName;
            State = state;
            RawValue = rawValue;
            UnknownItems = unknownItems;
        }

        /// <summary>The C# property.</summary>
        public string PropertyName { get; }

        /// <summary>The RouterOS field.</summary>
        public string FieldName { get; }

        /// <summary>Whether the router printed the field, and whether it could be read.</summary>
        public TikFieldState State { get; }

        /// <summary>The router's word when <see cref="State"/> is Unparsed.</summary>
        public string? RawValue { get; }

        /// <summary>On a <see cref="TikValueList{T}"/>: the items the type cannot hold, comma-separated as the router printed them; else <c>null</c>.</summary>
        public string? UnknownItems { get; }

        /// <inheritdoc/>
        public override string ToString()
            => FieldName + ": " + State
               + (RawValue != null ? " '" + RawValue + "'" : "")
               + (UnknownItems != null ? " +" + UnknownItems : "");
    }

    /// <summary>
    /// Thrown by <see cref="TikFieldStrictnessExtensions.EnsureStrict{TEntity}(TEntity, TikStrictness, string[])"/>; lists every
    /// offending field.
    /// </summary>
    public class TikStrictValueException : InvalidOperationException
    {
        /// <summary>The fields that failed the check.</summary>
        public IReadOnlyList<TikFieldReportItem> Offenders { get; }

        /// <summary>.ctor</summary>
        public TikStrictValueException(string entityName, IReadOnlyList<TikFieldReportItem> offenders)
            : base(entityName + ": " + string.Join("; ", offenders.Select(o => o.ToString())))
        {
            Offenders = offenders;
        }
    }

    /// <summary>
    /// Makes the value states of an entity visible: a report of every <see cref="TikField{T}"/> property, and a check that
    /// throws when a value the caller relies on was not read. A load never fails on a value from another RouterOS version;
    /// these are how a caller finds out that it happened.
    /// </summary>
    [RequiresUnreferencedCode(TikTrimming.MapperMessage)]
    [RequiresDynamicCode(TikTrimming.DynamicCodeMessage)]
    public static class TikFieldStrictnessExtensions
    {
        /// <summary>The state of every <see cref="TikField{T}"/> property of <paramref name="entity"/>, in declaration order.</summary>
        public static IReadOnlyList<TikFieldReportItem> GetValueReport<TEntity>(this TEntity entity)
        {
            if (entity == null)
                throw new ArgumentNullException(nameof(entity));
            var metadata = TikEntityMetadataCache.GetMetadata<TEntity>();
            var report = new List<TikFieldReportItem>();
            foreach (var property in metadata.Properties.Where(p => p.IsWrapped))
            {
                var value = (ITikField)property.GetWrapped(entity);
                report.Add(new TikFieldReportItem(property.PropertyName, property.FieldName, value.State, value.RawValue,
                    (value.BoxedValue as ITikValueList)?.UnknownItems));
            }
            return report;
        }

        /// <summary>
        /// Throws <see cref="TikStrictValueException"/>, naming every offending field, when a <see cref="TikField{T}"/>
        /// property of <paramref name="entity"/> is in a state <paramref name="strictness"/> refuses; returns the entity
        /// otherwise. Use it after a load whose values you act on.
        /// </summary>
        /// <param name="entity">The entity to check.</param>
        /// <param name="strictness">What to refuse; by default a value that could not be read.</param>
        /// <param name="allowAbsent">Fields (RouterOS names) <see cref="TikStrictness.Absent"/> does not apply to.</param>
        public static TEntity EnsureStrict<TEntity>(this TEntity entity, TikStrictness strictness = TikStrictness.Unparsed,
            params string[] allowAbsent)
        {
            var offenders = Offenders(entity, strictness, allowAbsent);
            if (offenders.Count > 0)
                throw new TikStrictValueException(typeof(TEntity).Name, offenders);
            return entity;
        }

        /// <summary>
        /// The same check over every entity of a loaded list; throws on the first offending row, returns the list. A name of
        /// its own: an overload of <c>EnsureStrict</c> would lose to the single-entity one for an array or a <c>List</c>.
        /// </summary>
        public static IEnumerable<TEntity> EnsureAllStrict<TEntity>(this IEnumerable<TEntity> entities,
            TikStrictness strictness = TikStrictness.Unparsed, params string[] allowAbsent)
        {
            var list = entities as IList<TEntity> ?? entities.ToList();
            foreach (var entity in list)
                entity.EnsureStrict(strictness, allowAbsent);
            return list;
        }

        private static List<TikFieldReportItem> Offenders<TEntity>(TEntity entity, TikStrictness strictness, string[] allowAbsent)
            => entity.GetValueReport()
                .Where(item =>
                    (strictness.HasFlag(TikStrictness.Unparsed) && item.State == TikFieldState.Unparsed)
                    || (strictness.HasFlag(TikStrictness.Absent) && item.State == TikFieldState.Absent
                        && !allowAbsent.Contains(item.FieldName, StringComparer.OrdinalIgnoreCase))
                    || (strictness.HasFlag(TikStrictness.UnknownListItems) && item.UnknownItems != null))
                .ToList();
    }
}
