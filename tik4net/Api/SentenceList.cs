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
        private readonly Dictionary<string, List<ITikSentence>> _sentencesForTags = new Dictionary<string, List<ITikSentence>>();

        internal bool TryDequeue(string tag, out ITikSentence sentence)
        {
            if (tag == "")
                tag = EMPTY_TAG_KEY;
            lock(_lockObj)
            {
                List<ITikSentence> list;
                if (_sentencesForTags.TryGetValue(tag, out list))
                {
                    if (list.Count > 0)
                    {
                        sentence = list[0];
                        list.RemoveAt(0);

                        if (list.Count <= 0)
                            _sentencesForTags.Remove(tag); //free memory

                        return true;
                    }
                    else
                    {
                        //empty list - should not happen
                        sentence = null;
                        return false;
                    }

                }
                else
                {
                    sentence = null;
                    return false;
                }
            }
        }

        internal void Enqueue(ITikSentence sentence)
        {
            lock(_lockObj)
            {
                List<ITikSentence> list;
                string sentenceTag = StringHelper.IsNullOrWhiteSpace(sentence.Tag) ? EMPTY_TAG_KEY : sentence.Tag;
                if (!_sentencesForTags.TryGetValue(sentenceTag, out list))
                {
                    list = new List<ITikSentence>();
                    _sentencesForTags.Add(sentenceTag, list);
                }

                list.Add(sentence);
            }
        }
    }
}
