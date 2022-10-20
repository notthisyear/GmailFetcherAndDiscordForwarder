using System;
using System.Threading.Tasks;
using System.Collections.Generic;
using GoogleMailFetcher.Common;

namespace GoogleMailFetcher
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

            var mailClient = new GmailClient(accountName, arguments.EmailAddress, arguments.CredentialsPath!);
            LoggerType.Internal.Log(LoggingLevel.Info, "Initializing Gmail service...");
            await mailClient.Initialize(ServiceName);

            LoggerType.Internal.Log(LoggingLevel.Info, "Getting e-mails...");
            (List<string>? ids, Exception? e) = await mailClient.TryGetAllEmailIdsInInbox();
            if (e != default)
            {
                LoggerType.GoogleCommunication.Log(LoggingLevel.Error, e);
                return;
            }

            LoggerType.Internal.Log(LoggingLevel.Info, $"Found {ids!.Count} {(ids!.Count == 1 ? "email" : "emails")}, fetching content...");
            (List<GoogleEmail>? emails, e) = await mailClient.TryGetAllEmailsFromIds(ids!);
            if (e != default)
            {
                LoggerType.GoogleCommunication.Log(LoggingLevel.Error, e);
                return;
            }

            Console.ReadLine();
        }
    }
}
