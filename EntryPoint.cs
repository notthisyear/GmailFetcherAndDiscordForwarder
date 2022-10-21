using System;
using System.Threading.Tasks;
using System.Collections.Generic;
using GmailFetcherAndForwarder.Common;

namespace GmailFetcherAndForwarder
{
    internal class EntryPoint
    {
        private const string ServiceName = "GoogleMailFetcher";

        public static async Task StartProgram(GoogleMailFetcherArguments arguments)
        {
            LoggerType.Internal.Log(LoggingLevel.Info, $"{ServiceName} started, targeting account '{arguments.EmailAddress}'");

            var accountName = arguments.EmailAddress!.Split('@')[0];
            if (string.IsNullOrEmpty(accountName))
                throw new InvalidOperationException("Could not parse email address");

            LoggerType.Internal.Log(LoggingLevel.Warning, "TODO: Read local cache");

            using var mailClient = new GmailClient(accountName, arguments.EmailAddress, arguments.CredentialsPath!);
            LoggerType.Internal.Log(LoggingLevel.Info, "Initializing Gmail service...");
            await mailClient.Initialize(ServiceName);

            (bool success, List<GmailEmail>? receivedEmails, List<GmailEmail>? sentEmails) = await TryGetAllReceivedAndSentEmails(mailClient);
            if (!success)
                return;

            Console.ReadLine();
        }

        private static async Task<(bool success, List<GmailEmail>? received, List<GmailEmail>? sent)> TryGetAllReceivedAndSentEmails(GmailClient mailClient)
        {
            (bool success, List<string>? receivedEmailsIds) = await TryGetReceivedEmailIds(mailClient);
            if (!success)
                return (false, default, default);

            (success, List<string>? sentEmailsIds) = await TryGetSentEmailIds(mailClient);
            if (!success)
                return (false, default, default);

            List<GmailEmail>? receivedEmails, sentEmails;
            Exception? e;

            (receivedEmails, e) = await GetContentFromEmailIds(mailClient, receivedEmailsIds!, "received", showProgress: true);
            if (e != default)
            {
                LoggerType.GoogleCommunication.Log(LoggingLevel.Error, e);
                return (false, default, default);
            }

            (sentEmails, e) = await GetContentFromEmailIds(mailClient, sentEmailsIds!, "sent", showProgress: true);
            if (e != default)
            {
                LoggerType.GoogleCommunication.Log(LoggingLevel.Error, e);
                return (false, default, default);
            }

            return (true, receivedEmails, sentEmails);

        }

        private static async Task<(bool success, List<string>? ids)> TryGetReceivedEmailIds(GmailClient mailClient)
        {
            LoggerType.Internal.Log(LoggingLevel.Info, "Getting received e-mail IDs...");
            (List<string>? ids, Exception? e) = await mailClient.TryGetAllEmailIdsInInbox();
            if (e != default)
                LoggerType.GoogleCommunication.Log(LoggingLevel.Error, e);
            return (e == default, e == default ? ids : default);
        }

        private static async Task<(bool success, List<string>? ids)> TryGetSentEmailIds(GmailClient mailClient)
        {
            LoggerType.Internal.Log(LoggingLevel.Info, "Getting sent e-mail IDs...");
            (List<string>? ids, Exception? e) = await mailClient.TryGetAllSentEmails();
            if (e != default)
                LoggerType.GoogleCommunication.Log(LoggingLevel.Error, e);
            return (e == default, e == default ? ids : default);
        }

        private static async Task<(List<GmailEmail>? emails, Exception? e)> GetContentFromEmailIds(GmailClient client, List<string> ids, string mailType, bool showProgress = false)
        {
            LoggerType.Internal.Log(LoggingLevel.Info, $"Found {ids.Count} {(ids.Count == 1 ? "e-mail" : "e-mails")} of type '{mailType}', fetching content...");
            (List<GmailEmail>? emails, Exception? e) = await client.TryGetEmailsFromIds(ids!, showProgress);
            return (e == default ? emails : default, e);
        }
    }
}
