using Xunit;
using GmailFetcherAndDiscordForwarder.Common;

namespace GmailFetcherAndDiscordForwarderTests
{
    public class MiscTests
    {
        [Theory]
        [InlineData("011bd66", "011bd66")]
        [InlineData("011bd66-dirty", "011bd66 (dirty)")]
        [InlineData("v1.0.0-0-g011bd66", "v1.0.0")]
        [InlineData("v1.0.0-0-g011bd66-dirty", "v1.0.0 [testd]")]
        [InlineData("v1.0.0-3-g011bd66-dirty", "v1.0.0 [testd-3] (011bd66)")]
        [InlineData("v1.0.0-21-g011bd66", "v1.0.0 [test-21] (011bd66)")]
        internal void VerifyGitInformationFormatter(string rawContent, string expectedOutput)
        {
            Assert.True(GitInformation.TryParseGitInformation(rawContent, out GitInformation? info));
            Assert.Equal(expectedOutput, info!.ToString());
        }
    }
}
