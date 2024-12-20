﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using GmailFetcherAndDiscordForwarder.Common;
using GmailFetcherAndDiscordForwarder.Gmail;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;

namespace GmailFetcherAndDiscordForwarder
{
    internal class CacheManager
    {
        #region Private fields
        private readonly List<GmailEmail> _emails;
        private readonly Dictionary<string, string> _messageIdToThreadIdMap;
        private readonly string _emailCachePath;
        private readonly string _idMappingCachePath;

        private static readonly JsonSerializerSettings s_serializerSettings = new()
        {
            ContractResolver = new DefaultContractResolver { NamingStrategy = new SnakeCaseNamingStrategy() },
            Formatting = Formatting.Indented
        };
        #endregion

        public CacheManager(string emailCachePath, string idMappingCachePath)
        {
            if (!File.Exists(emailCachePath))
            {
                using var s = File.Create(emailCachePath);
            }

            if (!File.Exists(idMappingCachePath))
            {
                using var s = File.Create(idMappingCachePath);
            }

            _emailCachePath = emailCachePath;
            _idMappingCachePath = idMappingCachePath;

            _emails = new();
            _messageIdToThreadIdMap = new();
        }

        #region Public methods
        public List<GmailEmail> GetEmails()
            => _emails;

        public bool HasEmails()
            => _emails.Any();

        public bool TryLookupInIdCache(string messageId, out string? threadId)
            => _messageIdToThreadIdMap.TryGetValue(messageId, out threadId);

        public void ReadEmailCacheFromDisk()
        {
            LoggerType.Internal.Log(LoggingLevel.Info, $"Checking e-mail cache...");
            if (TryReadCacheFromDisk(_emailCachePath, out List<GmailEmail>? emails))
            {
                LoggerType.Internal.Log(LoggingLevel.Info, $"Read {emails!.Count} e-mails from cache");
                AddToCache(emails!);
            }
            else
            {
                LoggerType.Internal.Log(LoggingLevel.Info, $"No e-mail cache available");
            }
        }

        public void ReadIdMappingCacheFromDisk()
        {
            if (TryReadCacheFromDisk(_idMappingCachePath, out Dictionary<string, string>? idMapping))
            {
                foreach (var entry in idMapping!)
                    _messageIdToThreadIdMap.Add(entry.Key, entry.Value);
            }
        }

        public void FlushEmailCacheToDisk()
            => FlushCacheToDisk(_emails, _emailCachePath);

        public void FlushIdMappingToDisk()
            => FlushCacheToDisk(_messageIdToThreadIdMap, _idMappingCachePath);

        public void AddToCache(List<GmailEmail> newEmails)
        {
            foreach (var email in newEmails)
            {
                if (!email.IsValid || email.Date == DateTime.MinValue)
                    continue;
                _emails!.Add(email);
            }
        }

        public void AddToCache(string messageId, string threadId)
        {
            if (!_messageIdToThreadIdMap.ContainsKey(messageId))
                _messageIdToThreadIdMap.Add(messageId, threadId);
        }

        public List<string> GetMailIdsOfType(MailType type)
            => _emails.Where(x => x.MailType == type).Select(x => x.MailId).ToList();

        public void Clear()
        {
            _emails.Clear();
            _messageIdToThreadIdMap.Clear();
        }
        #endregion

        #region Private methods
        private static bool TryReadCacheFromDisk<T>(string path, out T? result)
        {
            string json;
            string fileName = Path.GetFileName(path);
            result = default;
            try
            {
                json = File.ReadAllText(path);
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
                result = JsonConvert.DeserializeObject<T>(json, s_serializerSettings);
                if (result == null)
                    LoggerType.Internal.Log(LoggingLevel.Warning, $"Deserialization of file '{fileName}' returned null");
                return result != null;
            }
            catch (JsonException e)
            {
                LoggerType.Internal.Log(LoggingLevel.Warning, $"Failed to deserialize from '{fileName}' - {e.FormatException()}");
                return false;
            }
        }

        private static void FlushCacheToDisk<T>(T cacheInstance, string path)
        {
            string json;
            try
            {
                json = JsonConvert.SerializeObject(cacheInstance, s_serializerSettings);
            }
            catch (JsonException e)
            {
                LoggerType.Internal.Log(LoggingLevel.Warning, $"Failed to serialize e-mail list - {e.FormatException()}");
                return;
            }

            if (!string.IsNullOrEmpty(json))
            {
                try
                {
                    File.WriteAllText(path, json);
                }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException or FileNotFoundException)
                {
                    LoggerType.Internal.Log(LoggingLevel.Warning, $"Writing to file '{path}' failed - {e.FormatException()}");
                }
            }
        }
        #endregion
    }
}
