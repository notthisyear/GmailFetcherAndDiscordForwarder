﻿using System;
using System.Linq;
using System.Collections.Generic;
using NLog;
using GmailFetcherAndDiscordForwarder.Common;

namespace GmailFetcherAndDiscordForwarder.Gmail
{
    internal class GmailMailManager : IDisposable
    {
        public event EventHandler<(GmailThread thread, GmailEmail email)>? NewEmailInThread;
        public event EventHandler<GmailEmail>? NewEmail;

        #region Private fields
        private readonly CacheManager _cacheManager;
        private readonly Dictionary<string, GmailEmail> _standaloneEmails = new();
        private readonly List<GmailThread> _threads = new();
        private bool _disposedValue;
        #endregion

        public GmailMailManager(CacheManager cacheManager)
        {
            _cacheManager = cacheManager;
        }

        #region Public methods
        public void Initialize()
        {
            if (!_cacheManager.HasEmails())
                return;

            var cachedEmails = _cacheManager.GetEmails();

            // Emails that have something in the "In-Reply-To" part are leafs in a thread
            var emailsInReply = cachedEmails.Where(x => !string.IsNullOrEmpty(x.InReplyTo));
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
                GmailEmail? root = BuildThreadBackwardsFromEmail(cachedEmails, currentThread, email);
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
            foreach (var email in cachedEmails)
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
            List<GmailEmail> emailsWithUnknownReference = new();
            foreach (var email in validNewEmails)
            {
                if (!string.IsNullOrEmpty(email.InReplyTo))
                {
                    // Since we sorted all new e-mails according to date, this *should* always work
                    // However, the Date header can be forged or incorrect - never trust outside data. 
                    if (TryParseEmailWithReference(email, out GmailThread? thread))
                        NewEmailInThread?.Invoke(this, (thread!, email));
                    else
                        emailsWithUnknownReference.Add(email);
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

            // If we got an unknown reference, the referenced email may have been added by now
            foreach (var email in emailsWithUnknownReference)
            {
                if (TryParseEmailWithReference(email, out GmailThread? thread))
                {
                    NewEmailInThread?.Invoke(this, (thread!, email));
                    continue;
                }

                LoggerType.GoogleCommunication.Log(LoggingLevel.Warning, $"New e-mail (id: '{email.MessageId}') refers to unknown parent (id: '{email.InReplyTo}')");
                if (!_standaloneEmails.ContainsKey(email.MessageId))
                {
                    _standaloneEmails.Add(email.MessageId, email);
                    NewEmail?.Invoke(this, email);
                }
            }
        }
        #endregion

        #region Private methods
        private bool TryParseEmailWithReference(GmailEmail email, out GmailThread? matchingThread)
        {
            matchingThread = _threads.FirstOrDefault(x => email.InReplyTo!.Equals(x.CurrentLeafId, StringComparison.Ordinal));
            if (matchingThread != default)
            {
                matchingThread.AddLeaf(email);
                LoggerType.GoogleCommunication.Log(LoggingLevel.Info, $"New e-mail added to e-mail thread '{matchingThread.ThreadRootId}'");
            }
            else
            {
                // This can be the first response to an e-mail that previously had no children
                if (_standaloneEmails.TryGetValue(email.InReplyTo!, out var parentEmail))
                {
                    _standaloneEmails.Remove(parentEmail.MessageId);
                    var newThread = new GmailThread(parentEmail, new List<GmailEmail>());
                    newThread.AddLeaf(email);
                    _threads.Add(newThread);
                    LoggerType.GoogleCommunication.Log(LoggingLevel.Info, $"New e-mail added to e-mail new thread '{newThread.ThreadRootId}'");
                    matchingThread = newThread;
                }
            }
            return matchingThread != default;
        }

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
