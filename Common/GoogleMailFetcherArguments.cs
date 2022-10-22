using CommandLine;

namespace GmailFetcherAndForwarder.Common
{
    internal class GoogleMailFetcherArguments
    {

        [Option(longName: "credentials-path", shortName: 'c', Required = true, HelpText = "Path to the credentials file")]
        public string? CredentialsPath { get; set; }

        [Option(longName: "email-address", shortName: 'e', Required = true, HelpText = "The Google email address to target")]
        public string? EmailAddress { get; set; }

        [Option(longName: "received-emails-cache", shortName: 'r', HelpText = "Path to cached received e-mails (as JSON)")]
        public string? ReceivedEmailsCachePath { get; set; }

        [Option(longName: "sent-emails-cache", shortName: 's', Required = true, HelpText = "Path to cached sent e-mails (as JSON)")]
        public string? SentEmailsCachePath { get; set; }
    }
}
