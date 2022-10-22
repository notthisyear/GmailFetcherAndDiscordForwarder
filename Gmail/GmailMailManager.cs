using System;
using System.Linq;
using System.Collections.Generic;
using NLog;
using GmailFetcherAndForwarder.Common;

namespace GmailFetcherAndForwarder.Gmail
{
    internal class GmailMailManager : IDisposable
    {
        #region Private fields
        private readonly Dictionary<string, GmailEmail> _standaloneEmails = new();
        private readonly List<GmailThread> _threads = new();
        private bool _disposedValue;
        #endregion

        public event EventHandler<(string threadRootId, GmailEmail email)>? NewEmailInThread;
        public event EventHandler<GmailEmail>? NewEmail;

        #region Public methods
        public void Initialize(List<GmailEmail> emails)
        {
            // Emails that have something in the "In-Reply-To" part are leafs in a thread
            var emailsInReply = emails.Where(x => !string.IsNullOrEmpty(x.InReplyTo));
            if (!emailsInReply.Any())
                return;

            HashSet<string> seenEmails = new();
            foreach (var email in emailsInReply)
            {
                if (seenEmails.Contains(email.MessageId))
                    continue;

                var matchingThread = _threads.FirstOrDefault(x => email.InReplyTo!.Equals(x.CurrentLeafId, StringComparison.Ordinal));
                if (matchingThread != default)
                {
                    matchingThread.AddLeaf(email);
                    seenEmails.Add(email.MessageId);
                    continue;
                }

                var currentThread = new List<GmailEmail>();
                GmailEmail? root = BuildThreadBackwardsFromEmail(emails, currentThread, email);
                if (root == default)
                    LoggerType.Internal.Log(LoggingLevel.Debug, $"Could not find referenced email '{email.InReplyTo}'");

                var newThread = new GmailThread(root ?? email, currentThread);
                if (root != default)
                {
                    seenEmails.Add(root.MessageId);
                    newThread.AddLeaf(email);
                }

                seenEmails.Add(email.MessageId);
                currentThread.ForEach(x => seenEmails.Add(x.MessageId));
                _threads.Add(newThread);
            }

            // All unseen e-mails at this point are standalone emails, i.e. not part of a message thread (yet)
            foreach (var email in emails)
            {
                if (seenEmails.Contains(email.MessageId))
                    continue;

                // There can be clones in the sent/received folder
                if (!_standaloneEmails.ContainsKey(email.MessageId))
                    _standaloneEmails.Add(email.MessageId, email);
            }
        }

        public void ProcessNewEmails(List<GmailEmail> newEmails)
        {
            var validNewEmails = newEmails.Where(x => x.IsValid).ToList();
            int numberOfInvalidEmails = newEmails.Count - validNewEmails.Count;

            if (numberOfInvalidEmails != 0)
                LoggerType.GoogleCommunication.Log(LoggingLevel.Warning, $"{numberOfInvalidEmails} new {(numberOfInvalidEmails == 1 ? "e-mail" : "e-mails")} could not be parsed");

            validNewEmails.Sort((a, b) => a.Date == b.Date ? 0 : (a.Date > b.Date ? 1 : -1));
            foreach (var email in validNewEmails)
            {
                if (!string.IsNullOrEmpty(email.InReplyTo))
                {
                    var matchingThread = _threads.FirstOrDefault(x => email.InReplyTo!.Equals(x.CurrentLeafId, StringComparison.Ordinal));
                    if (matchingThread != default)
                    {
                        matchingThread.AddLeaf(email);
                        LoggerType.GoogleCommunication.Log(LoggingLevel.Info, $"New e-mail added to e-mail thread '{matchingThread.ThreadRootId}'");
                        NewEmailInThread?.Invoke(this, (matchingThread.ThreadRootId, email));
                    }
                    else
                    {
                        // This can be the first response to an e-mail that previously had no children
                        if (_standaloneEmails.TryGetValue(email.InReplyTo, out var parentEmail))
                        {
                            _standaloneEmails.Remove(parentEmail.MessageId);
                            var newThread = new GmailThread(parentEmail, new List<GmailEmail>());
                            newThread.AddLeaf(email);
                            _threads.Add(newThread);
                            LoggerType.GoogleCommunication.Log(LoggingLevel.Info, $"New e-mail added to e-mail new thread '{newThread.ThreadRootId}'");
                            NewEmailInThread?.Invoke(this, (newThread.ThreadRootId, email));
                        }
                        else
                        {
                            // Since we sorted all new e-mails according to date, we *should* never end up here
                            // However, never trust outside data
                            LoggerType.GoogleCommunication.Log(LoggingLevel.Warning, $"New e-mail (id: '{email.MessageId}') refers to unknown parent (id: '{email.InReplyTo}')");
                            if (!_standaloneEmails.ContainsKey(email.MessageId))
                            {
                                _standaloneEmails.Add(email.MessageId, email);
                                NewEmail?.Invoke(this, email);
                            }
                        }
                    }
                }
                else
                {
                    LoggerType.GoogleCommunication.Log(LoggingLevel.Info, $"New e-mail (id: '{email.MessageId}') does not refer to a parent");
                    if (!_standaloneEmails.ContainsKey(email.MessageId))
                    {
                        _standaloneEmails.Add(email.MessageId, email);
                        NewEmail?.Invoke(this, email);
                    }
                }
            }
        }
        #endregion

        #region Private methods
        private GmailEmail? BuildThreadBackwardsFromEmail(List<GmailEmail> mailCache, List<GmailEmail> currentThread, GmailEmail email)
        {
            var parent = mailCache.FirstOrDefault(x => x.MessageId.Equals(email.InReplyTo, StringComparison.Ordinal));
            if (parent == default)
            {
                return default;
            }
            else if (string.IsNullOrEmpty(parent.InReplyTo))
            {
                return parent;
            }
            else
            {
                var parentsParent = BuildThreadBackwardsFromEmail(mailCache, currentThread, parent);
                currentThread.Add(parent);
                return parentsParent;
            }
        }
        #endregion

        #region Disposal
        protected virtual void Dispose(bool disposing)
        {
            if (!_disposedValue)
            {
                if (disposing)
                {

                }

                _standaloneEmails.Clear();
                foreach (var thread in _threads)
                    thread.Clear();
                _threads.Clear();

                _disposedValue = true;
            }
        }

        public void Dispose()
        {
            Dispose(disposing: true);
            GC.SuppressFinalize(this);
        }
        #endregion
    }
}
