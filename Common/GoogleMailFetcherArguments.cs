using CommandLine;

namespace GmailFetcherAndForwarder.Common
{
    internal class GoogleMailFetcherArguments
    {

        [Option(longName: "credentials-path", shortName: 'c', Required = true, HelpText = "Path to the credentials file")]
        public string? CredentialsPath { get; set; }

        [Option(longName: "email-address", shortName: 'e', Required = true, HelpText = "The Google email address to target")]
        public string? EmailAddress { get; set; }

        [Option(longName: "email-cache", HelpText = "Path to cached e-mails (as JSON)")]
        public string? EmailsCachePath { get; set; }
    }
}
