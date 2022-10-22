using System;
using System.Net.Http;
using System.Threading;
using System.Text.Json;
using System.Net.Http.Json;
using System.Threading.Tasks;
using NLog;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;
using GmailFetcherAndDiscordForwarder.Common;
using GmailFetcherAndDiscordForwarder.Gmail;

namespace GmailFetcherAndDiscordForwarder.Discord
{
    internal class DiscordClient : IDisposable
    {
        #region Private fields
        private enum QueryParameter
        {
            Wait,
            ThreadId
        }

        private struct ExecuteWebhookParams
        {
            public string ThreadName { get; set; }

            public string Content { get; set; }
        }

        private struct CreateThreadResponse
        {
            public string Id { get; set; }
        }

        private readonly string _discordWebhook;
        private readonly CacheManager _cacheManager;
        private readonly GmailMailManager _mailManager;
        private readonly AutoResetEvent _discordPostingLock = new(true);

        private bool _disposedValue;

        // Note: It would be nice to not have to use System.Json here, as we're using Newtonsoft everywhere else...
        private static readonly JsonNamingPolicy s_snakeCaseNamingPolicy = new SnakeCaseNamingPolicy();
        private static readonly JsonSerializerOptions s_serializerOptions = new() { PropertyNamingPolicy = s_snakeCaseNamingPolicy };
        #endregion

        public DiscordClient(string discordWebhook, CacheManager cacheManager, GmailMailManager mailManager)
        {
            _discordWebhook = discordWebhook;
            _cacheManager = cacheManager;
            _mailManager = mailManager;

            _mailManager.NewEmail += NewEmailReceived;
            _mailManager.NewEmailInThread += NewEmailInThreadReceived;
        }

        #region Private methods
        private void NewEmailReceived(object? sender, GmailEmail e)
        {
            Task.Run(async () =>
            {
                _discordPostingLock.WaitOne();
                _ = await TryMakeNewPost(e);
                _discordPostingLock.Set();
            }).ConfigureAwait(false);
        }

        private void NewEmailInThreadReceived(object? sender, (string threadRootMessageId, GmailEmail email) e)
        {

            Task.Run(async () =>
            {
                _discordPostingLock.WaitOne();
                _ = await TryAddMessageToThread(e.threadRootMessageId, e.email);
                _discordPostingLock.Set();
            }).ConfigureAwait(false);
        }

        private async Task<bool> TryMakeNewPost(GmailEmail email)
        {
            if (_cacheManager.TryLookupInIdCache(email.MessageId, out _))
            {
                LoggerType.DiscordCommunication.Log(LoggingLevel.Warning, $"Cannot add message with id '{email.MessageId}' to Discord - already present in cache!");
                return false;
            }

            // Discord has a 2000 character limit for the content field
            using HttpClient client = new HttpClient();
            ExecuteWebhookParams webHookParams = new()
            {
                ThreadName = email.Subject,
                Content = email.GetContentFormatted()
            };

            var httpContent = JsonContent.Create(webHookParams, options: s_serializerOptions);
            var response = await client.PostAsync($"{_discordWebhook}{AddQueryParameter(QueryParameter.Wait, true)}", httpContent);

            if (!response.IsSuccessStatusCode)
            {
                LoggerType.DiscordCommunication.Log(LoggingLevel.Warning, $"Could not create new thread - {response.FormatHttpResponse()}");
                return false;
            }

            var responseContent = await response.Content.ReadAsStringAsync();
            if (string.IsNullOrEmpty(responseContent))
            {
                LoggerType.DiscordCommunication.Log(LoggingLevel.Warning, "Could not read response content");
                return false;
            }

            CreateThreadResponse createThreadResponse;
            try
            {
                createThreadResponse = JsonConvert.DeserializeObject<CreateThreadResponse>(responseContent,
                settings: new() { ContractResolver = new DefaultContractResolver { NamingStrategy = new SnakeCaseNamingStrategy() } });
            }
            catch (Newtonsoft.Json.JsonException e)
            {
                LoggerType.DiscordCommunication.Log(LoggingLevel.Warning, $"Could not deserialize response content - {e.FormatException()}");
                return false;
            }

            if (string.IsNullOrEmpty(createThreadResponse.Id))
            {
                LoggerType.DiscordCommunication.Log(LoggingLevel.Warning, "Discord returned empty thread id");
                return false;
            }

            _cacheManager.AddToCache(email.MessageId, createThreadResponse.Id);
            return true;
        }

        private async Task<bool> TryAddMessageToThread(string threadRootMessageId, GmailEmail email)
        {
            if (!_cacheManager.TryLookupInIdCache(threadRootMessageId, out string? threadId))
            {
                LoggerType.DiscordCommunication.Log(LoggingLevel.Warning, $"Cannot add message with id '{email.MessageId}' to Discord - already present in cache!");
                return false;
            }

            // Discord has a 2000 character limit for the content field
            using HttpClient client = new HttpClient();
            ExecuteWebhookParams webHookParams = new()
            {
                Content = email.GetContentFormatted()
            };

            var httpContent = JsonContent.Create(webHookParams, options: s_serializerOptions);
            var response = await client.PostAsync($"{_discordWebhook}{AddQueryParameter(QueryParameter.ThreadId, threadId!)}", httpContent);

            if (!response.IsSuccessStatusCode)
            {
                LoggerType.DiscordCommunication.Log(LoggingLevel.Warning, $"Could not create post in in thread '{threadId}' - {response.FormatHttpResponse()}");
                return false;
            }

            return true;
        }

        private static string AddQueryParameter(QueryParameter parameter, dynamic value)
        {
            if (parameter == QueryParameter.Wait && value is bool b)
                return $"?wait={(b ? "true" : "false")}";
            else if (parameter == QueryParameter.ThreadId && value is string s)
                return $"?thread_id={s}";
            else
                return string.Empty;
        }
        #endregion

        #region Disposal
        protected virtual void Dispose(bool disposing)
        {
            if (!_disposedValue)
            {
                if (disposing)
                {
                    _mailManager.NewEmail -= NewEmailReceived;
                    _mailManager.NewEmailInThread -= NewEmailInThreadReceived;
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
