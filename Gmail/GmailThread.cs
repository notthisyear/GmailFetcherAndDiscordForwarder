using System.Linq;
using System.Collections.Generic;

namespace GmailFetcherAndDiscordForwarder.Gmail
{
    internal class GmailThread
    {
        #region Public properties
        public int Count => _thread.Count;

        public string ThreadRootId => _thread.First().MessageId;
        #endregion

        private readonly List<GmailEmail> _thread;

        public GmailThread(GmailEmail root, List<GmailEmail> thread)
        {
            _thread = new() { root };
            foreach (var email in thread)
                AddLeaf(email);
        }

        #region Public methods
        public void AddLeaf(GmailEmail email)
        {
            _thread.Add(email);
        }

        public void Clear()
        {
            _thread.Clear();
        }

        public string CurrentLeafId => _thread.Any() ? _thread.Last().MessageId : string.Empty;
        #endregion
    }
}
