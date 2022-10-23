using System;
using System.Net.Http;
using System.Net.Sockets;
using GmailFetcherAndDiscordForwarder.Gmail;

namespace GmailFetcherAndDiscordForwarder.Common
{
    internal static class GeneralExtensionMethods
    {
        public enum Emphasis
        {
            Italic,
            Bold
        }
        public static string FormatException(this Exception e)
        {
            if (e is SocketException socketException)
                return $"{socketException.GetType()} (socket error code: {socketException.ErrorCode}): {e.Message}";
            else
                return $"{e.GetType()}: {e.Message}";
        }

        public static string FormatHttpResponse(this HttpResponseMessage response)
            => $"HTTP status code {response.StatusCode}: {response.ReasonPhrase}";

        public static string AsString(this MailType type)
            => $"{type.ToString().ToLower()}";

        public static string SanitizeThreadNameForDiscord(this string s)
            => s.Replace("@", "").Replace("#", "").Replace("\"", "").Replace("/", "").Replace("\\", "").Replace("<", "").Replace(">", "").Replace(':', ';');

        public static string AddMarkdownEmphasis(this string s, Emphasis emphasis, bool addSpaceAfter = false)
           => emphasis switch
           {
               Emphasis.Italic => $"*{s}*{(addSpaceAfter ? " " : "")}",
               Emphasis.Bold => $"**{s}**{(addSpaceAfter ? " " : "")}",
               _ => throw new NotImplementedException(),
           };
    }
}
