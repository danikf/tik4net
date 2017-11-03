using System.Collections.Generic;

namespace InvertedTomato.TikLink {
    public class Sentence {
        /// <summary>
        /// Command-word if being sent, or reply-word if being received from router.
        /// </summary>
        /// <remarks>
        /// The following are the possible received reply-words:
        ///   !re  This is a record
        ///   !done Query was successful
        ///   !trap  Query failed or was interrupted
        ///   !fatal  Connection to be closed
        /// </remarks>
        public string Command { get; set; }

        /// <summary>
        /// Attribute key-value pairs. The "=" will automatically be added when sending and removed when receiving.
        /// </summary>
        public Dictionary<string, string> Attributes { get; set; } = new Dictionary<string, string>();

        /// <summary>
        /// Query words. The "?" will automatically be added when sending and removed when receiving.
        /// </summary>
        /// <remarks>
        /// See https://wiki.mikrotik.com/wiki/Manual:API#Queries for details
        /// </remarks>
        public List<string> Queries { get; set; } = new List<string>();
    }
}