using CommandLine;

namespace GmailFetcherAndForwarder.Common
{
    internal class GoogleMailFetcherArguments
    {

        [Option(longName: "credentials-path", shortName: 'c', Required = true, HelpText = "Path to the credentials file")]
        public string? CredentialsPath { get; set; }

        [Option(longName: "email-address", shortName: 'e', Required = true, HelpText = "The Google email address to target")]
        public string? EmailAddress { get; set; }

        [Option(longName: "fetching-interval", Default = 5, HelpText = "The interval with which to check the account for new emails (in minutes)")]
        public int EmailFetchingIntervalMinutes { get; set; }

        [Option(longName: "email-cache", HelpText = "Path to cached e-mails (as JSON). If omitted, a new cache will be created in the current users home directory")]
        public string? EmailsCachePath { get; set; }
    }
}
