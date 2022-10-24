using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using GmailFetcherAndDiscordForwarder.Common;
using GmailFetcherAndDiscordForwarder.Discord;
using GmailFetcherAndDiscordForwarder.Gmail;

namespace GmailFetcherAndDiscordForwarder
{
    internal class EntryPoint
    {
        private const string ServiceName = "GmailFetcherAndDiscordForwarder";
        private static readonly CancellationTokenSource s_cts = new();

        public static async Task StartProgram(GmailFetcherAndDiscordForwarderArguments arguments)
        {
            LoggerType.Internal.Log(LoggingLevel.Info, $"{ServiceName} started, targeting account '{arguments.EmailAddress}'");

            var accountName = arguments.EmailAddress!.Split('@')[0];
            if (string.IsNullOrEmpty(accountName))
                throw new InvalidOperationException("Could not parse email address");


            using var mailClient = new GmailClient(accountName, arguments.EmailAddress, arguments.CredentialsPath!);
            LoggerType.Internal.Log(LoggingLevel.Info, "Initializing Gmail service...");
            await mailClient.Initialize(ServiceName);

            if (string.IsNullOrEmpty(arguments.EmailsCachePath))
                arguments.EmailsCachePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), $"{accountName}_email_cache.json");

            if (string.IsNullOrEmpty(arguments.ThreadIdMappingCachePath))
                arguments.ThreadIdMappingCachePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), $"{accountName}_thread_id_cache.json");


            LoggerType.Internal.Log(LoggingLevel.Info, "Initializing cache manager...");
            var cacheManager = new CacheManager(arguments.EmailsCachePath, arguments.ThreadIdMappingCachePath);
            cacheManager.ReadEmailCacheFromDisk();
            cacheManager.ReadIdMappingCacheFromDisk();

            LoggerType.Internal.Log(LoggingLevel.Info, "Initializing mail manager...");
            using var mailManager = new GmailMailManager(cacheManager);
            mailManager.Initialize();

            if (!arguments.OnlyBuildEmailCache)
            {
                LoggerType.Internal.Log(LoggingLevel.Info, "Creating Discord service...");
                using DiscordClient discordClient = new(arguments.DiscordWebhookUri!, cacheManager, mailManager);

                TaskCompletionSource tcs = new();
                Console.CancelKeyPress += ApplicationClosing;

                _ = Task.Run(async () =>
                {
                    await MonitorGmailServer(60 * 1000 * arguments.EmailFetchingIntervalMinutes, mailClient, cacheManager, mailManager, s_cts.Token);
                    tcs.SetResult();
                });

                await tcs.Task;
            }
            else
            {
                var newEmails = await mailClient.GetAllNewEmails(
                    cacheManager.GetMailIdsOfType(MailType.Received),
                    cacheManager.GetMailIdsOfType(MailType.Sent));
                cacheManager.AddToCache(newEmails);
                cacheManager.FlushEmailCacheToDisk();
            }

            cacheManager.Clear();
            s_cts.Dispose();

            LoggerType.Internal.Log(LoggingLevel.Info, "Application closing");
        }

        private static async Task MonitorGmailServer(int fetchingIntervalMs, GmailClient mailClient, CacheManager cacheManager, GmailMailManager mailManager, CancellationToken ct)
        {
            bool firstTime = true;
            while (true)
            {
                if (!firstTime)
                {
                    var nextServerFetch = DateTime.Now.AddMilliseconds(fetchingIntervalMs);
                    LoggerType.Internal.Log(LoggingLevel.Info, $"Next server fetch at {nextServerFetch:ddd dd MMM HH:mm:ss}");

                    try
                    {
                        await Task.Delay(fetchingIntervalMs, ct);
                    }
                    catch (OperationCanceledException)
                    {
                        return;
                    }
                }
                else
                {
                    firstTime = false;
                }

                LoggerType.GoogleCommunication.Log(LoggingLevel.Info, "Fetching new e-mails from server...");
                var newEmails = await mailClient.GetAllNewEmails(
                    cacheManager.GetMailIdsOfType(MailType.Received),
                    cacheManager.GetMailIdsOfType(MailType.Sent), ct);

                if (newEmails.Any())
                {
                    mailManager.ProcessNewEmails(newEmails);
                    cacheManager.AddToCache(newEmails);
                    cacheManager.FlushEmailCacheToDisk();
                }
                else
                {
                    LoggerType.GoogleCommunication.Log(LoggingLevel.Info, "No new e-mails found");
                }

                if (ct.IsCancellationRequested)
                    return;
            }
        }

        private static void ApplicationClosing(object? sender, ConsoleCancelEventArgs e)
        {
            Console.CancelKeyPress -= ApplicationClosing;
            s_cts.Cancel();
            e.Cancel = true;
        }
    }
}
