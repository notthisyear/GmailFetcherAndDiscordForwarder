using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Collections.Generic;
using Newtonsoft.Json;
using GmailFetcherAndForwarder.Common;
using GmailFetcherAndForwarder.Gmail;
using Newtonsoft.Json.Serialization;

namespace GmailFetcherAndForwarder
{
    internal class EntryPoint
    {
        private const string ServiceName = "GmailFetcherAndForwarder";

        public static async Task StartProgram(GoogleMailFetcherArguments arguments)
        {
            LoggerType.Internal.Log(LoggingLevel.Info, $"{ServiceName} started, targeting account '{arguments.EmailAddress}'");

            var accountName = arguments.EmailAddress!.Split('@')[0];
            if (string.IsNullOrEmpty(accountName))
                throw new InvalidOperationException("Could not parse email address");


            using var mailClient = new GmailClient(accountName, arguments.EmailAddress, arguments.CredentialsPath!);
            LoggerType.Internal.Log(LoggingLevel.Info, "Initializing Gmail service...");
            await mailClient.Initialize(ServiceName);

            bool success;
            List<GmailEmail>? receivedEmails, sentEmails;

            (success, receivedEmails) = await TryGetEmailsFromCacheOrServer(MailType.Received, arguments.ReceivedEmailsCachePath, mailClient);
            if (!success)
                receivedEmails = new();

            (success, sentEmails) = await TryGetEmailsFromCacheOrServer(MailType.Sent, arguments.SentEmailsCachePath, mailClient);
            if (!success)
                sentEmails = new();

            Console.ReadLine();
            return;
        }

        private static async Task<(bool success, List<GmailEmail>? emails)> TryGetEmailsFromCacheOrServer(MailType type, string? receivedEmailsCachePath, GmailClient mailClient)
        {
            List<GmailEmail>? emailsOrNull;
            if (!string.IsNullOrEmpty(receivedEmailsCachePath))
            {
                LoggerType.Internal.Log(LoggingLevel.Info, $"Reading {type.AsString()} e-mail cache...");
                if (TryReadEmailListFromFile(receivedEmailsCachePath!, out emailsOrNull))
                {
                    LoggerType.Internal.Log(LoggingLevel.Info, $"Read {emailsOrNull!.Count} {type.AsString()} e-mails from cache");
                    return (true, emailsOrNull);
                }
            }
            else
            {
                LoggerType.Internal.Log(LoggingLevel.Info, $"No {type.AsString()} e-mail cache available, fetching from server...");
            }

            (List<string>? emailsIds, Exception? e) = type switch
            {
                MailType.Sent => await mailClient.TryGetAllSentEmails(),
                MailType.Received => await mailClient.TryGetAllReceivedEmails(),
                _ => throw new NotImplementedException(),
            };

            if (e != default)
            {
                LoggerType.GoogleCommunication.Log(LoggingLevel.Error, e);
                return (false, default);
            }
            LoggerType.Internal.Log(LoggingLevel.Info, $"Found {emailsIds!.Count} {(emailsIds.Count == 1 ? "e-mail" : "e-mails")} of type '{type.AsString()}', fetching content...");

            (emailsOrNull, e) = await mailClient.TryGetEmailsFromIds(type, emailsIds!, showProgress: true);
            if (e != default)
                LoggerType.GoogleCommunication.Log(LoggingLevel.Error, e);

            return (e == default, emailsOrNull);
        }

        private static bool TryReadEmailListFromFile(string filePath, out List<GmailEmail>? emails)
        {
            emails = default;
            string json;

            string fileName = Path.GetFileName(filePath);
            try
            {
                json = File.ReadAllText(filePath);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or FileNotFoundException)
            {
                LoggerType.Internal.Log(LoggingLevel.Warning, $"Reading of file '{fileName}' failed - {e.FormatException()}");
                return false;
            }

            if (string.IsNullOrEmpty(json))
            {
                LoggerType.Internal.Log(LoggingLevel.Warning, $"File '{fileName}' is empty");
                return false;
            }

            try
            {
                var settings = new JsonSerializerSettings
                {
                    ContractResolver = new DefaultContractResolver { NamingStrategy = new SnakeCaseNamingStrategy() },
                    Formatting = Formatting.Indented
                };
                emails = JsonConvert.DeserializeObject<List<GmailEmail>?>(json, settings);
                bool success = emails != default;
                if (!success)
                    LoggerType.Internal.Log(LoggingLevel.Warning, $"Deserialization of file '{fileName}' returned null");
                return success;
            }
            catch (JsonException e)
            {
                LoggerType.Internal.Log(LoggingLevel.Warning, $"Failed to deserialize from '{fileName}' - {e.FormatException()}");
                return false;
            }
        }
    }
}
