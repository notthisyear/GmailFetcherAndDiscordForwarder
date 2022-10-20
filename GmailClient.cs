using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Google.Apis.Auth.OAuth2;
using Google.Apis.Gmail.v1;
using Google.Apis.Services;
using Google.Apis.Util.Store;
using Google.Apis.Gmail.v1.Data;
using GoogleMailFetcher.Common;

namespace GoogleMailFetcher
{
    internal class GmailClient
    {
        #region Private fields
        private const string InboxLabelId = "INBOX";
        private const string DateHeaderPartName = "Date";
        private const string FromHeaderPartName = "From";
        private const string SubjectHeaderPartName = "Subject";
        private const string ReturnPathHeaderPartName = "Return-Path";
        private const string ReferencesHeaderPartName = "References";
        private const string InReplyToHeaderPartName = "In-Reply-To";

        private readonly string _accountName;
        private readonly string _emailAddress;
        private readonly string _credentialsPath;

        private GmailService? _mailService;
        #endregion

        public GmailClient(string accountName, string emailAddress, string credentialsPath)
        {
            _accountName = accountName;
            _emailAddress = emailAddress;
            _credentialsPath = credentialsPath;
        }

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

        public async Task<(List<string>? ids, Exception? e)> TryGetAllEmailIdsInInbox()
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
                    emailListRequest.LabelIds = InboxLabelId;
                    emailListRequest.IncludeSpamTrash = false;
                    if (!string.IsNullOrEmpty(pageToken))
                        emailListRequest.PageToken = pageToken;

                    var emailListResponse = await emailListRequest.ExecuteAsync();
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

        public async Task<(List<GoogleEmail>? emails, Exception? e)> TryGetAllEmailsFromIds(List<string> emailIds)
        {
            if (_mailService == default)
                return (default, new InvalidOperationException("Mail client not initialized"));

            if (!emailIds.Any())
                return (new List<GoogleEmail>(), default);

            try
            {
                List<GoogleEmail> result = new();
                foreach (var id in emailIds)
                {
                    var emailInfoRequest = _mailService.Users.Messages.Get(_emailAddress, id);
                    var emailInfoResponse = await emailInfoRequest.ExecuteAsync();

                    if (emailInfoResponse != null)
                    {
                        var date = emailInfoResponse.Payload.Headers.FirstOrDefault(x => x.Name.Equals(DateHeaderPartName, StringComparison.OrdinalIgnoreCase))?.Value;
                        var from = emailInfoResponse.Payload.Headers.FirstOrDefault(x => x.Name.Equals(FromHeaderPartName, StringComparison.OrdinalIgnoreCase))?.Value;
                        var subject = emailInfoResponse.Payload.Headers.FirstOrDefault(x => x.Name.Equals(SubjectHeaderPartName, StringComparison.OrdinalIgnoreCase))?.Value;
                        var returnPath = emailInfoResponse.Payload.Headers.FirstOrDefault(x => x.Name.Equals(ReturnPathHeaderPartName, StringComparison.OrdinalIgnoreCase))?.Value;
                        var references = emailInfoResponse.Payload.Headers.FirstOrDefault(x => x.Name.Equals(ReferencesHeaderPartName, StringComparison.OrdinalIgnoreCase))?.Value;
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
                            GoogleEmail.ConstructEmpty() : GoogleEmail.Construct(id, from, date, subject, returnPath, references, inReplyTo, bodyRaw));
                    }
                    else
                    {
                        return (default, new InvalidGmailResponseException($"Response for mail ID {id} was null"));
                    }
                }

                return (result, default);
            }
            catch (Exception e)
            {
                return (default, new GmailCommunicationException("Email fetching failed", e));
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
    }
}
