using InvertedTomato.TikLink.Records;
using System;
using System.Collections.Generic;
using System.Text;
using System.Linq;

namespace InvertedTomato.TikLink.RecordHandlers {
    public abstract class OrderedSetRecordHandlerBase<T> : SetRecordHandlerBase<T> where T : SetRecordBase, new() {
        internal OrderedSetRecordHandlerBase(Link link) : base(link) { }

        /// <summary>
        /// Move a set of records before another record.
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="before">ID of record to move before. Set as NULL to make the item last.</param>
        /// <param name="records">One or more IDs to move.</param>
        public void Move(T before, params T[] records) {
            if (null != before && null == before.Id) {
                throw new ArgumentException("ID cannot be null.", nameof(before));
            }
            if (records.Length == 0) {
                throw new ArgumentException("Must be at least one record provided.", nameof(records));
            }


            // Build sentence
            var sentence = new Sentence();
            sentence.Command = RecordReflection.GetPath<T>() + "/move";
            sentence.Attributes["numbers"] = string.Join(",", records.SelectMany(a => a.Id));
            if (null != before) {
                sentence.Attributes["destination"] = before.Id;
            };

            // Make call
            var result = Link.Call(sentence).Wait();
            if (result.IsError) {
                result.TryGetTrapAttribute("message", out var message);
                throw new CallException(message);
            }
        }
    }
}
