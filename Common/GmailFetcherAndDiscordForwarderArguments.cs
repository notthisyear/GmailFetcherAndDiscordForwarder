using CommandLine;

namespace GmailFetcherAndDiscordForwarder.Common
{
    internal class GmailFetcherAndDiscordForwarderArguments
    {

        [Option(longName: "credentials-path", Required = true, HelpText = "Path to the credentials file")]
        public string? CredentialsPath { get; set; }

        [Option(longName: "email-address", Required = true, HelpText = "The Google e-mail address to target")]
        public string? EmailAddress { get; set; }

        [Option(longName: "discord-webhook-uri", HelpText = "A Discord webhook URI to sent emails to (not required if --only-build-email-cache is set")]
        public string? DiscordWebhookUri { get; set; }

        [Option(longName: "fetching-interval", Default = 5, HelpText = "The interval with which to check the account for new e-mails (in minutes)")]
        public int EmailFetchingIntervalMinutes { get; set; }

        [Option(longName: "email-cache", HelpText = "Path to cached e-mails (as JSON). If omitted, a new cache will be created in the current users home directory")]
        public string? EmailsCachePath { get; set; }

        [Option(longName: "thread-id-mapping-cache", HelpText = "Path to cached thread ID and message ID map (as JSON). If omitted, a new cache will be created in the current users home directory")]
        public string? ThreadIdMappingCachePath { get; set; }

        [Option(longName: "only-build-email-cache", Required = false, Default = false, HelpText = "Only build e-mail cache and save to disk")]
        public bool OnlyBuildEmailCache { get; set; }


    }
}
