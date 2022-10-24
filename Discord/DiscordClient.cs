using System;
using System.Text;
using System.Net.Http;
using System.Threading;
using System.Text.Json;
using System.Net.Http.Json;
using System.Threading.Tasks;
using System.Collections.Generic;
using NLog;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;
using GmailFetcherAndDiscordForwarder.Common;
using GmailFetcherAndDiscordForwarder.Gmail;
using static GmailFetcherAndDiscordForwarder.Common.GeneralExtensionMethods;

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

        // Discord has a 2000 character limit for the content field
        private const int MaxContentLengthPerPost = 2000;
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

        private void NewEmailInThreadReceived(object? sender, (GmailThread thread, GmailEmail email) e)
        {
            Task.Run(async () =>
            {
                _discordPostingLock.WaitOne();
                _ = await TryAddMessageToThread(e.thread.ThreadRootId, e.email, e.thread);
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

            ExecuteWebhookParams webHookParams = new()
            {
                ThreadName = $"{email.Subject} ({email.Date:yyyy-MM-dd})".SanitizeThreadNameForDiscord(),
                Content = GetFirstPostContent(email.Date)
            };

            using HttpClient client = new();
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
            _cacheManager.FlushIdMappingToDisk();

            LoggerType.DiscordCommunication.Log(LoggingLevel.Info, $"New Discord thread with id '{createThreadResponse.Id}' created");

            return await TryAddMessageToThread(email.MessageId, email);
        }

        private async Task<bool> TryAddMessageToThread(string threadRootMessageId, GmailEmail email, GmailThread? thread = default)
        {
            if (!_cacheManager.TryLookupInIdCache(threadRootMessageId, out string? threadId))
            {
                if (thread == default)
                {
                    LoggerType.DiscordCommunication.Log(LoggingLevel.Warning, $"Cannot add message with id '{email.MessageId}' to Discord - thread id missing");
                    return false;
                }
                else
                {
                    return await PostMissingThread(thread);
                }
            }

            List<string>? posts = GetEmailAsPosts(email);

            if (posts == default)
            {
                LoggerType.DiscordCommunication.Log(LoggingLevel.Warning, $"Could not generate Discord posts for e-mail '{email.MessageId}'");
                return false;
            }

            using HttpClient client = new();
            ExecuteWebhookParams webHookParams = new();
            foreach (var post in posts)
            {
                webHookParams.Content = post;
                var httpContent = JsonContent.Create(webHookParams, options: s_serializerOptions);
                var response = await client.PostAsync($"{_discordWebhook}{AddQueryParameter(QueryParameter.ThreadId, threadId!)}", httpContent);

                if (!response.IsSuccessStatusCode)
                {
                    LoggerType.DiscordCommunication.Log(LoggingLevel.Warning, $"Could not create post in in thread '{threadId}' - {response.FormatHttpResponse()}");
                    return false;
                }

            }

            LoggerType.DiscordCommunication.Log(LoggingLevel.Info, $"E-mail '{email.MessageId}' added to Discord thread '{threadId}' (generated {posts.Count} {(posts.Count == 1 ? "post" : "posts")})");
            return true;
        }

        private static List<string>? GetEmailAsPosts(GmailEmail email)
        {
            var header = GetMailHeader(email.Subject, email.From, email.Date);
            var content = email.GetAsContentAsPlainText();

            if (string.IsNullOrEmpty(content))
                return default;

            int totalLength = header.Length + content.Length;
            int numberOfPosts = (int)Math.Ceiling(totalLength / (double)MaxContentLengthPerPost);
            bool isMultiPost = numberOfPosts > 1;

            StringBuilder sb = new();
            sb.Append(header);
            List<string> result = new();

            if (!isMultiPost)
            {
                sb.Append(content);
                result.Add(sb.ToString());
                return result;
            }

            int numberOfPostsRemaining = numberOfPosts;
            int indexInContent = 0;

            // Each post will typically be somewhat shorter, as we search for a newline from the end
            int currentPostMaxLength = Math.Min(MaxContentLengthPerPost, (int)(totalLength / (double)numberOfPosts));
            int remainingLength = totalLength;

            while (numberOfPostsRemaining > 0)
            {
                if (isMultiPost)
                {
                    string currentPostInfo = $"({numberOfPosts - numberOfPostsRemaining + 1}/{numberOfPosts})";
                    sb.AppendLine(currentPostInfo);
                    currentPostMaxLength -= currentPostInfo.Length;
                }

                string currentContentSlice = GetContentSlice(content, currentPostMaxLength, ref indexInContent);
                sb.Append(currentContentSlice);
                result.Add(sb.ToString());

                remainingLength -= currentContentSlice.Length;
                numberOfPostsRemaining--;

                currentPostMaxLength =
                    numberOfPostsRemaining == 1 ? MaxContentLengthPerPost :
                    Math.Min(MaxContentLengthPerPost, (int)(remainingLength / (double)numberOfPostsRemaining));

                sb.Clear();
            }

            return result;
        }

        private async Task<bool> PostMissingThread(GmailThread thread)
        {
            bool success = await TryMakeNewPost(thread.GetRoot());
            if (!success)
                return false;

            foreach (var leaf in thread.GetLeafs())
            {
                success = await TryAddMessageToThread(thread.ThreadRootId, leaf);
                if (!success)
                    break;
            }
            return success;
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

        private static string GetFirstPostContent(DateTime emailDate)
        {
            StringBuilder sb = new();
            sb.Append("first e-mail received at".AddMarkdownEmphasis(Emphasis.Italic, addSpaceAfter: true));
            sb.Append($"{emailDate:yyyy-MM-dd HH:mm:ss}".AddMarkdownEmphasis(Emphasis.Bold, addSpaceAfter: true));
            sb.Append("thread created at".AddMarkdownEmphasis(Emphasis.Italic, addSpaceAfter: true));
            sb.Append($"{DateTime.Now:yyyy-MM-dd HH:mm:ss}".AddMarkdownEmphasis(Emphasis.Bold));
            return sb.ToString();
        }

        public static string GetMailHeader(string subject, string from, DateTime emailDate)
        {
            StringBuilder sb = new();
            sb.AppendLine(subject.AddMarkdownEmphasis(Emphasis.Bold));
            sb.Append("e-mail sent from".AddMarkdownEmphasis(Emphasis.Italic, addSpaceAfter: true));
            sb.Append(from.AddMarkdownEmphasis(Emphasis.Bold, addSpaceAfter: true));
            sb.Append("at".AddMarkdownEmphasis(Emphasis.Italic, addSpaceAfter: true));
            sb.AppendLine($"{emailDate:yyyy-MM-dd HH:mm:ss}".AddMarkdownEmphasis(Emphasis.Bold));
            sb.AppendLine("---\n");

            return sb.ToString();
        }

        private static string GetContentSlice(string content, int maxLengthPerPost, ref int indexInContent)
        {
            int currentIdx = indexInContent;
            int lowestTolerableIdx = currentIdx + (int)(maxLengthPerPost * 0.9);

            // The entire remaing content will fit
            if (maxLengthPerPost + currentIdx >= content.Length)
            {
                indexInContent = content.Length - 1;
                return content[currentIdx..];
            }

            // Search from the end until a newline or ' '
            int blankspaceIdx = -1;
            for (int i = currentIdx + maxLengthPerPost - 1; i >= lowestTolerableIdx; i--)
            {
                if (content[i] == '\n')
                {
                    indexInContent = i + 1;
                    return content[currentIdx..i];
                }
                else if (content[i] == ' ' && blankspaceIdx == -1)
                {
                    blankspaceIdx = i;
                }
            }

            // No newline
            if (blankspaceIdx != -1)
            {
                indexInContent = blankspaceIdx + 1;
                return content[currentIdx..blankspaceIdx];
            }

            // No "good" split point found, nothing more to do
            indexInContent += maxLengthPerPost;
            return content[currentIdx..(indexInContent - 1)];
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
