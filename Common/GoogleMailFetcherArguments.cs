using CommandLine;

namespace GoogleMailFetcher.Common
{
    internal class GoogleMailFetcherArguments
    {

        [Option(longName: "credentials-path", shortName: 'c', Required = true, HelpText = "Path to the credentials file")]
        public string? CredentialsPath { get; set; }

        [Option(longName: "account-name", shortName: 'a', Required = true, HelpText = "The name of the Google account")]
        public string? AccountName { get; set; }
    }
}
