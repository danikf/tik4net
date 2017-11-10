using InvertedTomato.TikLink.Records;
using System;
using System.Collections.Generic;
using System.Text;

namespace InvertedTomato.TikLink.RecordHandlers {
    public abstract class OrderedSetRecordHandlerBase<T> : SetRecordHandlerBase<T> where T : SetRecordBase, new() {
        internal OrderedSetRecordHandlerBase(Link link) : base(link) { }

        /// <summary>
        /// Move a set of records before another record.
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="beforeId">ID of record to move before. Set as NULL to make the item last.</param>
        /// <param name="ids">One or more IDs to move.</param>
        public void Move(string beforeId, params string[] ids) {
            if (ids.Length == 0) {
                throw new ArgumentException("Must be at least one id provided.", nameof(ids));
            }

            // Build sentence
            var sentence = new Sentence();
            sentence.Command = RecordReflection.GetPath<T>() + "/move";
            sentence.Attributes["numbers"] = string.Join(",", ids);
            if (null != beforeId) {
                sentence.Attributes["destination"] = beforeId;
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
