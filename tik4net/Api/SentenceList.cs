using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace tik4net.Api
{
    internal class SentenceList
    {
        private const string EMPTY_TAG_KEY = "-empty-";
        private readonly object _lockObj = new object();
        private readonly Dictionary<string, Queue<ITikSentence>> _sentencesForTags = new Dictionary<string, Queue<ITikSentence>>();

        internal bool TryDequeue(string tag, out ITikSentence sentence)
        {
            if (tag == "")
                tag = EMPTY_TAG_KEY;
            lock(_lockObj)
            {
                Queue<ITikSentence> queue;
                if (_sentencesForTags.TryGetValue(tag, out queue) && queue.Count > 0)
                {
                    sentence = queue.Dequeue();
                    if (queue.Count == 0)
                        _sentencesForTags.Remove(tag);
                    return true;
                }
                sentence = null;
                return false;
            }
        }

        internal void Enqueue(ITikSentence sentence)
        {
            lock(_lockObj)
            {
                Queue<ITikSentence> queue;
                string sentenceTag = string.IsNullOrWhiteSpace(sentence.Tag) ? EMPTY_TAG_KEY : sentence.Tag;
                if (!_sentencesForTags.TryGetValue(sentenceTag, out queue))
                {
                    queue = new Queue<ITikSentence>();
                    _sentencesForTags.Add(sentenceTag, queue);
                }
                queue.Enqueue(sentence);
            }
        }
    }
}
