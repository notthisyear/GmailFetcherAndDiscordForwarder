using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;
using NLog;
using Google.Apis.Gmail.v1;
using Google.Apis.Services;
using Google.Apis.Util.Store;
using Google.Apis.Auth.OAuth2;
using Google.Apis.Gmail.v1.Data;
using GmailFetcherAndDiscordForwarder.Common;

namespace GmailFetcherAndDiscordForwarder.Gmail
{
    internal class GmailClient : IDisposable
    {
        #region Private fields
        private const string InboxLabelId = "INBOX";
        private const string SentLabelId = "SENT";

        private const string MessageIdHeaderPartName = "Message-ID";
        private const string DateHeaderPartName = "Date";
        private const string FromHeaderPartName = "From";
        private const string ToHeaderPartName = "To";
        private const string SubjectHeaderPartName = "Subject";
        private const string ReturnPathHeaderPartName = "Return-Path";
        private const string InReplyToHeaderPartName = "In-Reply-To";

        private readonly string _accountName;
        private readonly string _emailAddress;
        private readonly string _credentialsPath;

        private GmailService? _mailService;
        private bool _disposedValue;
        #endregion

        public GmailClient(string accountName, string emailAddress, string credentialsPath)
        {
            _accountName = accountName;
            _emailAddress = emailAddress;
            _credentialsPath = credentialsPath;
        }

        #region Public methods
        public async Task Initialize(string serviceName)
        {
            var localFileStorePath = Path.GetDirectoryName(_credentialsPath);
            using var stream = new FileStream(_credentialsPath, FileMode.Open, FileAccess.Read);
            UserCredential credential = await GoogleWebAuthorizationBroker.AuthorizeAsync(GoogleClientSecrets.FromStream(stream).Secrets,
                new[] { GmailService.Scope.GmailReadonly },
                _accountName,
                CancellationToken.None,
                new FileDataStore(localFileStorePath, fullPath: true)
            );

            _mailService = new GmailService(new BaseClientService.Initializer()
            {
                HttpClientInitializer = credential,
                ApplicationName = serviceName
            });
        }

        public async Task<List<GmailEmail>> GetAllNewEmails(List<string> existingReceivedEmailIds, List<string> existingSentEmailIds)
            => await GetAllNewEmails(existingReceivedEmailIds, existingSentEmailIds, CancellationToken.None);

        public async Task<List<GmailEmail>> GetAllNewEmails(List<string> existingReceivedEmailIds, List<string> existingSentEmailIds, CancellationToken ct)
        {
            List<string> newReceivedIds = await CheckForNewEmails(MailType.Received, existingReceivedEmailIds, ct);
            List<string> newSentIds = await CheckForNewEmails(MailType.Sent, existingSentEmailIds, ct);

            List<GmailEmail> newEmails = new();
            if (newReceivedIds.Any())
            {
                LoggerType.GoogleCommunication.Log(LoggingLevel.Info, $"Found {newReceivedIds!.Count} new '{MailType.Received.AsString()}' {(newReceivedIds.Count == 1 ? "e-mail" : "e-mails")}, fetching content...");
                (List<GmailEmail>? newReceivedEmails, Exception? e) = await TryGetEmailsFromIds(MailType.Received, newReceivedIds, showProgress: true);

                if (e != default)
                    LoggerType.GoogleCommunication.Log(LoggingLevel.Error, e, $"Could not get new {MailType.Received.AsString()} e-mail content");
                else
                    newReceivedEmails?.ForEach(x => newEmails.Add(x));
            }

            if (newSentIds.Any())
            {
                LoggerType.GoogleCommunication.Log(LoggingLevel.Info, $"Found {newSentIds!.Count} new '{MailType.Sent.AsString()}' {(newSentIds.Count == 1 ? "e-mail" : "e-mails")}, fetching content...");
                (List<GmailEmail>? newSentEmails, Exception? e) = await TryGetEmailsFromIds(MailType.Sent, newSentIds, showProgress: true);

                if (e != default)
                    LoggerType.GoogleCommunication.Log(LoggingLevel.Error, e, $"Could not get new {MailType.Sent.AsString()} e-mail content");
                else
                    newSentEmails?.ForEach(x => newEmails.Add(x));
            }

            return newEmails;

        }
        #endregion

        #region Private methods
        private async Task<List<string>> CheckForNewEmails(MailType type, IEnumerable<string> existingEmailIds, CancellationToken ct)
        {
            (List<string>? allEmailsIds, Exception? e) = type switch
            {
                MailType.Sent => await TryGetAllSentEmails(ct),
                MailType.Received => await TryGetAllReceivedEmails(ct),
                _ => throw new NotImplementedException(),
            };

            if (e != default)
            {
                if (!ct.IsCancellationRequested)
                    LoggerType.GoogleCommunication.Log(LoggingLevel.Error, e);
                return new();
            }

            List<string> newEmailIds = new();
            foreach (string id in allEmailsIds!)
            {
                if (!existingEmailIds.Where(x => x.Equals(id, StringComparison.Ordinal)).Any())
                    newEmailIds.Add(id);
            }
            return newEmailIds;
        }

        private async Task<(List<string>? ids, Exception? e)> TryGetAllReceivedEmails(CancellationToken ct)
            => await TryGetAllEmailsWithLabel(InboxLabelId, ct);

        private async Task<(List<string>? ids, Exception? e)> TryGetAllSentEmails(CancellationToken ct)
            => await TryGetAllEmailsWithLabel(SentLabelId, ct);

        private async Task<(List<GmailEmail>? emails, Exception? e)> TryGetEmailsFromIds(MailType type, List<string> emailIds, bool showProgress = false)
        {
            if (_mailService == default)
                return (default, new InvalidOperationException("Mail client not initialized"));

            if (!emailIds.Any())
                return (new List<GmailEmail>(), default);

            try
            {
                List<GmailEmail> result = new();
                var i = 1;

                if (showProgress)
                    LogManager.Flush();

                foreach (var id in emailIds)
                {
                    if (showProgress)
                        WriteStringToCurrentLine($"[{i++}/{emailIds.Count}] Fetching data for e-mail '{id}'...");

                    var emailInfoRequest = _mailService.Users.Messages.Get(_emailAddress, id);
                    var emailInfoResponse = await emailInfoRequest.ExecuteAsync();

                    if (emailInfoResponse != null)
                    {
                        var date = emailInfoResponse.Payload.Headers.FirstOrDefault(x => x.Name.Equals(DateHeaderPartName, StringComparison.OrdinalIgnoreCase))?.Value;
                        var messageId = emailInfoResponse.Payload.Headers.FirstOrDefault(x => x.Name.Equals(MessageIdHeaderPartName, StringComparison.OrdinalIgnoreCase))?.Value;
                        var from = emailInfoResponse.Payload.Headers.FirstOrDefault(x => x.Name.Equals(FromHeaderPartName, StringComparison.OrdinalIgnoreCase))?.Value;
                        var to = emailInfoResponse.Payload.Headers.FirstOrDefault(x => x.Name.Equals(ToHeaderPartName, StringComparison.OrdinalIgnoreCase))?.Value;
                        var subject = emailInfoResponse.Payload.Headers.FirstOrDefault(x => x.Name.Equals(SubjectHeaderPartName, StringComparison.OrdinalIgnoreCase))?.Value;
                        var returnPath = emailInfoResponse.Payload.Headers.FirstOrDefault(x => x.Name.Equals(ReturnPathHeaderPartName, StringComparison.OrdinalIgnoreCase))?.Value;
                        var inReplyTo = emailInfoResponse.Payload.Headers.FirstOrDefault(x => x.Name.Equals(InReplyToHeaderPartName, StringComparison.OrdinalIgnoreCase))?.Value;

                        var bodyRaw = string.Empty;
                        if (!string.IsNullOrEmpty(date) && !string.IsNullOrEmpty(from))
                        {
                            if (emailInfoResponse.Payload.Parts == null && emailInfoResponse.Payload.Body != null)
                                bodyRaw = emailInfoResponse.Payload.Body.Data;
                            else
                                bodyRaw = ExtractBodyByParts(emailInfoResponse.Payload.Parts);
                        }

                        result.Add(string.IsNullOrEmpty(bodyRaw) ?
                            GmailEmail.ConstructEmpty(type) : GmailEmail.Construct(type, id, from, to, date, messageId, subject, returnPath, inReplyTo, bodyRaw));
                    }
                    else
                    {
                        if (showProgress)
                            WriteStringToCurrentLine("");

                        return (default, new InvalidGmailResponseException($"Response for mail id '{id}' was null"));
                    }
                }

                if (showProgress)
                    WriteStringToCurrentLine("");

                return (result, default);
            }
            catch (Exception e)
            {
                return (default, new GmailCommunicationException("Email fetching failed", e));
            }
        }

        private async Task<(List<string>? ids, Exception? e)> TryGetAllEmailsWithLabel(string label, CancellationToken ct)
        {
            if (_mailService == default)
                return (default, new InvalidOperationException("Mail client not initialized"));

            try
            {
                var pageToken = "";
                List<string> result = new();
                do
                {
                    var emailListRequest = _mailService.Users.Messages.List(_emailAddress);
                    emailListRequest.LabelIds = label;
                    emailListRequest.IncludeSpamTrash = false;
                    if (!string.IsNullOrEmpty(pageToken))
                        emailListRequest.PageToken = pageToken;

                    ListMessagesResponse? emailListResponse;
                    try
                    {
                        emailListResponse = await emailListRequest.ExecuteAsync(ct);
                    }
                    catch (TaskCanceledException e)
                    {
                        return (default, e);
                    }

                    if (emailListResponse == null || emailListResponse.Messages == null)
                        return (default, new InvalidGmailResponseException("Email listing return null"));

                    foreach (Message? messages in emailListResponse.Messages)
                        result.Add(messages.Id);

                    pageToken = emailListResponse.NextPageToken;

                } while (!string.IsNullOrEmpty(pageToken));
                return (result, default);
            }
            catch (Exception e)
            {
                return (default, new GmailCommunicationException("Email listing failed", e));
            }

        }

        private static string ExtractBodyByParts(IList<MessagePart>? part, string currentBody = "")
        {
            var newBody = currentBody;
            if (part == null)
            {
                return newBody;
            }
            else
            {
                foreach (MessagePart parts in part)
                {
                    if (parts.Parts == null)
                    {
                        if (parts.Body != null && !string.IsNullOrEmpty(parts.Body.Data))
                        {
                            newBody += parts.Body.Data;
                        }
                    }
                    else
                    {
                        return ExtractBodyByParts(parts.Parts, newBody);
                    }
                }
                return newBody;
            }
        }

        private static void WriteStringToCurrentLine(string s)
        {
            try
            {
                (var left, var top) = Console.GetCursorPosition();
                Console.SetCursorPosition(0, top);
                if (left > 0)
                {
                    Console.Write(new string(' ', left));
                    Console.SetCursorPosition(0, top);
                }
                Console.Write(s);
            }
            catch (IOException) { }
        }
        #endregion

        #region Disposal
        protected virtual void Dispose(bool disposing)
        {
            if (!_disposedValue)
            {
                if (disposing)
                {
                    if (_mailService != default)
                        _mailService.Dispose();
                }
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
