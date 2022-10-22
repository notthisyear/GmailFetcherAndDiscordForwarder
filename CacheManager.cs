using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using GmailFetcherAndForwarder.Common;
using GmailFetcherAndForwarder.Gmail;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;

namespace GmailFetcherAndForwarder
{
    internal class CacheManager
    {
        public List<GmailEmail> Emails { get; }

        private readonly string _emailCachePath;
        private static readonly JsonSerializerSettings s_serializerSettings = new()
        {
            ContractResolver = new DefaultContractResolver { NamingStrategy = new SnakeCaseNamingStrategy() },
            Formatting = Formatting.Indented
        };

        public CacheManager(string emailCachePath)
        {
            if (!File.Exists(emailCachePath))
                File.Create(emailCachePath);

            _emailCachePath = emailCachePath;
            Emails = new();
        }

        public void ReadCacheFromDisk()
        {
            string json;
            string fileName = Path.GetFileName(_emailCachePath!);
            try
            {
                json = File.ReadAllText(_emailCachePath!);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or FileNotFoundException)
            {
                LoggerType.Internal.Log(LoggingLevel.Warning, $"Reading of file '{fileName}' failed - {e.FormatException()}");
                return;
            }

            if (string.IsNullOrEmpty(json))
            {
                LoggerType.Internal.Log(LoggingLevel.Warning, $"File '{fileName}' is empty");
                return;
            }

            try
            {
                var emails = JsonConvert.DeserializeObject<List<GmailEmail>?>(json, s_serializerSettings);
                if (emails != default)
                    AddToCache(emails!);
                else
                    LoggerType.Internal.Log(LoggingLevel.Warning, $"Deserialization of file '{fileName}' returned null");
            }
            catch (JsonException e)
            {
                LoggerType.Internal.Log(LoggingLevel.Warning, $"Failed to deserialize from '{fileName}' - {e.FormatException()}");
            }
        }

        public void FlushEmailCacheToDisk()
        {
            string json;
            try
            {
                json = JsonConvert.SerializeObject(Emails, s_serializerSettings);
            }
            catch (JsonException e)
            {
                LoggerType.Internal.Log(LoggingLevel.Warning, $"Failed to serialize email list - {e.FormatException()}");
                return;
            }

            if (!string.IsNullOrEmpty(json))
            {
                try
                {
                    File.WriteAllText(_emailCachePath!, json);
                }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException or FileNotFoundException)
                {
                    LoggerType.Internal.Log(LoggingLevel.Warning, $"Writing to file '{_emailCachePath!}' failed - {e.FormatException()}");
                }
            }
        }

        public void AddToCache(List<GmailEmail> newEmails)
        {
            newEmails.ForEach(x => Emails!.Add(x));
        }

        public List<string> GetMailIdsOfType(MailType type)
            => Emails.Where(x => x.MailType == type).Select(x => x.MailId).ToList();

        public void Clear()
        {
            Emails?.Clear();
        }
    }
}
