using System.Linq;
using System.Text.RegularExpressions;


namespace GmailFetcherAndDiscordForwarder.Common
{
    internal class GitInformation
    {
        #region Public properties
        public string? VersionTag { get; set; }

        public string? LastCommitHash { get; set; }

        public int NumberOfCommitsAhead { get; set; }

        public bool HasVersionTag => !string.IsNullOrEmpty(VersionTag);

        public bool? IsDirty { get; set; }
        #endregion

        public override string ToString()
        {
            if (!HasVersionTag)
                return $"{LastCommitHash}{(IsDirty == null ? " <dirty status unknown>" : (IsDirty == true ? " (dirty)" : string.Empty))}";

            if (NumberOfCommitsAhead == 0 && IsDirty == false)
                return VersionTag!;

            if (NumberOfCommitsAhead == 0 && IsDirty != false)
                return $"{VersionTag} [{GetTestStatusText()}]";

            if (NumberOfCommitsAhead > 0)
                return $"{VersionTag} [{GetTestStatusText()}-{NumberOfCommitsAhead}] ({LastCommitHash})";

            return "<invalid git information>";
        }

        public static bool TryParseGitInformation(string rawContent, out GitInformation? gitInformation)
        {
            bool hasVersionTag = Regex.IsMatch(rawContent, @"v\d+\.\d+\.\d+");
            var extractingRegex = hasVersionTag ?
                new Regex(@"(?<version>v\d+\.\d+\.\d+)-(?<nrofcommitsahead>\d+)(?<commithash>(\-g\w+)|(\w+))(?<isdirty>(-dirty)|())")
                : new Regex(@"(?<commithash>\w+)(?<isdirty>(-dirty)|())");

            gitInformation = default;
            var result = extractingRegex.Match(rawContent);

            if (!result.Success)
                return false;

            gitInformation = new();
            foreach (Group group in result.Groups.Cast<Group>())
            {
                switch (group.Name)
                {
                    case "version":
                        gitInformation.VersionTag = (group.Success ? group.Value : string.Empty);
                        break;
                    case "nrofcommitsahead":
                        gitInformation.NumberOfCommitsAhead = (group.Success ? int.Parse(group.Value) : (-1));
                        break;
                    case "commithash":
                        // Remove the preceding "-g" if present
                        if (group.Success)
                            gitInformation.LastCommitHash = hasVersionTag ? group.Value[2..] : group.Value;
                        else
                            gitInformation.LastCommitHash = string.Empty;
                        break;
                    case "isdirty":
                        gitInformation.IsDirty = (group.Success ? new bool?(!string.IsNullOrEmpty(group.Value)) : null);
                        break;
                }
            }
            return true;
        }

        private string GetTestStatusText()
            => IsDirty == null ? "<dirty status unknown>" : (IsDirty == true ? "testd" : "test");
    }
}
