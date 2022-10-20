using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;
using GoogleMailFetcher.Common;
using Google.Apis.Auth.OAuth2;
using Google.Apis.Gmail.v1;
using Google.Apis.Services;
using Google.Apis.Util.Store;
using Google.Apis.Gmail.v1.Data;

namespace GoogleMailFetcher
{
    internal class EntryPoint
    {
        private const string InboxLabelId = "INBOX";
        private const string DateHeaderPartName = "Date";
        private const string FromHeaderPartName = "From";
        private const string SubjectHeaderPartName = "Subject";

        public static async Task StartProgram(GoogleMailFetcherArguments arguments)
        {
            LoggerType.Internal.Log(LoggingLevel.Info, $"GoogleMailFetcher started, targeting account '{arguments.AccountName}'");

            var localFileStore = Path.GetDirectoryName(arguments.CredentialsPath);
            var emailAddress = $"{arguments.AccountName}@gmail.com";

            try
            {
                UserCredential credential;
                using var stream = new FileStream(arguments.CredentialsPath!, FileMode.Open, FileAccess.Read);
                credential = await GoogleWebAuthorizationBroker.AuthorizeAsync(GoogleClientSecrets.FromStream(stream).Secrets,
                    new[] { GmailService.Scope.GmailReadonly },
                    arguments.AccountName,
                    CancellationToken.None,
                    new FileDataStore(localFileStore, fullPath: true)
                );

                LoggerType.Internal.Log(LoggingLevel.Info, "Authentication flow completed, creating Gmail service..");
                var gmailService = new GmailService(new BaseClientService.Initializer()
                {
                    HttpClientInitializer = credential,
                    ApplicationName = "GoogleMailFetcher"
                });

                var emailListRequest = gmailService.Users.Messages.List(emailAddress);
                emailListRequest.LabelIds = InboxLabelId;
                emailListRequest.IncludeSpamTrash = false;
                //emailListRequest.Q = "is:unread";

                LoggerType.Internal.Log(LoggingLevel.Info, "Getting e-mails...");
                var emailListResponse = await emailListRequest.ExecuteAsync();

                if (emailListResponse != null && emailListResponse.Messages != null)
                {
                    foreach (var email in emailListResponse.Messages)
                    {
                        var emailInfoRequest = gmailService.Users.Messages.Get(emailAddress, email.Id);
                        var emailInfoResponse = await emailInfoRequest.ExecuteAsync();

                        if (emailInfoResponse != null)
                        {
                            var date = string.Empty;
                            var from = string.Empty;
                            var subject = string.Empty;
                            var bodyRaw = string.Empty;

                            foreach (var headerPart in emailInfoResponse.Payload.Headers)
                            {
                                if (headerPart.Name.Equals(DateHeaderPartName, StringComparison.OrdinalIgnoreCase))
                                    date = headerPart.Value;

                                if (headerPart.Name.Equals(FromHeaderPartName, StringComparison.OrdinalIgnoreCase))
                                    from = headerPart.Value;

                                if (headerPart.Name.Equals(SubjectHeaderPartName, StringComparison.OrdinalIgnoreCase))
                                    subject = headerPart.Value;

                                if (!string.IsNullOrEmpty(date) && !string.IsNullOrEmpty(from))
                                {
                                    if (emailInfoResponse.Payload.Parts == null && emailInfoResponse.Payload.Body != null)
                                        bodyRaw = emailInfoResponse.Payload.Body.Data;
                                    else
                                        bodyRaw = GetNestedParts(emailInfoResponse.Payload.Parts, "");

                                    DumpMessageToConsole(date, from, subject, DecodeBody(bodyRaw));
                                }
                            }
                        }
                    }
                }
            }
            catch (Exception e)
            {
                LoggerType.GoogleCommunication.Log(LoggingLevel.Error, e, "Failed to get inbox");
            }

            Console.ReadLine();
        }


        private static string GetNestedParts(IList<MessagePart>? part, string curr)
        {
            var str = curr;
            if (part == null)
            {
                return str;
            }
            else
            {
                foreach (var parts in part)
                {
                    if (parts.Parts == null)
                    {
                        if (parts.Body != null && !string.IsNullOrEmpty(parts.Body.Data))
                        {
                            str += parts.Body.Data;
                        }
                    }
                    else
                    {
                        return GetNestedParts(parts.Parts, str);
                    }
                }

                return str;
            }
        }

        private static string DecodeBody(string bodyRaw)
        {
            byte[] data = Convert.FromBase64String(bodyRaw.Replace('-', '+').Replace('_', '/'));
            return Encoding.UTF8.GetString(data);
        }

        private static void DumpMessageToConsole(string date, string from, string subject, string body)
        {
            Console.WriteLine($"From: '{from}'");
            Console.WriteLine($"Date: {date}");
            Console.WriteLine($"Subject: {subject}");
            Console.WriteLine("---");
            Console.WriteLine(body);
            Console.WriteLine("\n---\n");
        }

    }
}
