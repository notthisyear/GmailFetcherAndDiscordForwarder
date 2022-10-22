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
        private static readonly JsonSerializerSettings s_serializerSettings = new()
        {
            ContractResolver = new DefaultContractResolver { NamingStrategy = new SnakeCaseNamingStrategy() },
            Formatting = Formatting.Indented
        };

        public static async Task StartProgram(GoogleMailFetcherArguments arguments)
        {
            LoggerType.Internal.Log(LoggingLevel.Info, $"{ServiceName} started, targeting account '{arguments.EmailAddress}'");

            var accountName = arguments.EmailAddress!.Split('@')[0];
            if (string.IsNullOrEmpty(accountName))
                throw new InvalidOperationException("Could not parse email address");


            using var mailClient = new GmailClient(accountName, arguments.EmailAddress, arguments.CredentialsPath!);
            LoggerType.Internal.Log(LoggingLevel.Info, "Initializing Gmail service...");
            await mailClient.Initialize(ServiceName);

            List<GmailEmail> emails = await TryGetAllEmails(arguments.EmailsCachePath, mailClient);
            if (!FlushEmailsToCache(emails, arguments.EmailsCachePath!))
                LoggerType.Internal.Log(LoggingLevel.Warning, "Flushing to cache failed");

            Console.ReadLine();
            return;
        }
 
        private static async Task<List<GmailEmail>> TryGetAllEmails(string? emailCachePath, GmailClient mailClient)
        {
            List<GmailEmail>? emailsOrNull = default;
            if (!string.IsNullOrEmpty(emailCachePath))
            {
                LoggerType.Internal.Log(LoggingLevel.Info, $"Reading e-mail cache...");
                if (TryReadEmailListFromFile(emailCachePath!, out emailsOrNull))
                    LoggerType.Internal.Log(LoggingLevel.Info, $"Read {emailsOrNull!.Count} e-mails from cache");
            }
            else
            {
                LoggerType.Internal.Log(LoggingLevel.Info, $"No e-mail cache available");
            }

            var emails = emailsOrNull == default ? new List<GmailEmail>() : emailsOrNull!;

            List<string> newReceivedIds = await CheckForNewEmails(MailType.Received, mailClient, emails.Where(x => x.MailType == MailType.Received));
            List<string> newSentIds = await CheckForNewEmails(MailType.Sent, mailClient, emails.Where(x => x.MailType == MailType.Sent));

            if (newReceivedIds.Any())
            {
                LoggerType.Internal.Log(LoggingLevel.Info, $"Found {newReceivedIds!.Count} new '{MailType.Received.AsString()}' {(newReceivedIds.Count == 1 ? "e-mail" : "e-mails")}, fetching content...");
                (List<GmailEmail>? newReceivedEmails, Exception? e) = await mailClient.TryGetEmailsFromIds(MailType.Received, newReceivedIds, showProgress: true);

                if (e != default)
                    LoggerType.GoogleCommunication.Log(LoggingLevel.Error, e, $"Could not get new {MailType.Received.AsString()} e-mail content");
                else
                    newReceivedEmails?.ForEach(x => emails.Add(x));
            }

            if (newSentIds.Any())
            {
                LoggerType.Internal.Log(LoggingLevel.Info, $"Found {newSentIds!.Count} new '{MailType.Sent.AsString()}' {(newSentIds.Count == 1 ? "e-mail" : "e-mails")}, fetching content...");
                (List<GmailEmail>? newSentEmails, Exception? e) = await mailClient.TryGetEmailsFromIds(MailType.Sent, newSentIds, showProgress: true);

                if (e != default)
                    LoggerType.GoogleCommunication.Log(LoggingLevel.Error, e, $"Could not get new {MailType.Sent.AsString()} e-mail content");
                else
                    newSentEmails?.ForEach(x => emails.Add(x));

            }

            return emails;
        }

        private static async Task<List<string>> CheckForNewEmails(MailType type, GmailClient mailClient, IEnumerable<GmailEmail> existingEmails)
        {
            (List<string>? allEmailsIds, Exception? e) = type switch
            {
                MailType.Sent => await mailClient.TryGetAllSentEmails(),
                MailType.Received => await mailClient.TryGetAllReceivedEmails(),
                _ => throw new NotImplementedException(),
            };

            if (e != default)
            {
                LoggerType.GoogleCommunication.Log(LoggingLevel.Error, e);
                return new();
            }

            List<string> newEmailIds = new();
            foreach (string id in allEmailsIds!)
            {
                if (!existingEmails.Where(x => x.MailId.Equals(id, StringComparison.Ordinal)).Any())
                    newEmailIds.Add(id);
            }
            return newEmailIds;
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
                emails = JsonConvert.DeserializeObject<List<GmailEmail>?>(json, s_serializerSettings);
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

        private static bool FlushEmailsToCache(List<GmailEmail> emails, string emailsCachePath)
        {
            var json = string.Empty;
            try
            {
                json = JsonConvert.SerializeObject(emails, s_serializerSettings);
            }
            catch (JsonException e)
            {
                LoggerType.Internal.Log(LoggingLevel.Warning, $"Failed to serialize email list - {e.FormatException()}");
                return false;
            }

            if (!string.IsNullOrEmpty(json))
            {
                try
                {
                    File.WriteAllText(emailsCachePath, json);
                    return true;
                }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException or FileNotFoundException)
                {
                    LoggerType.Internal.Log(LoggingLevel.Warning, $"Writing to file '{emailsCachePath}' failed - {e.FormatException()}");
                    return false;
                }
            }

            return true;
        }
    }
}
