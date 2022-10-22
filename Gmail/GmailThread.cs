using System.Linq;
using System.Collections.Generic;

namespace GmailFetcherAndForwarder.Gmail
{
    internal class GmailThread
    {
        public int Count => _thread.Count;

        private readonly List<GmailEmail> _thread;

        public GmailThread(GmailEmail root, List<GmailEmail> thread)
        {
            _thread = new() { root };
            foreach (var email in thread)
                AddLeaf(email);
        }

        public void AddLeaf(GmailEmail email)
        {
            _thread.Add(email);
        }

        public void Clear()
        {
            _thread.Clear();
        }

        public string CurrentLeafId => _thread.Any() ? _thread.Last().MessageId : string.Empty;
    }
}
