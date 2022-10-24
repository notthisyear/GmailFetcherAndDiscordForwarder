using System;
using System.Net.Http;
using System.Net.Sockets;
using System.Reflection;
using System.ComponentModel;
using System.Collections.Generic;
using GmailFetcherAndDiscordForwarder.Gmail;

namespace GmailFetcherAndDiscordForwarder.Common
{
    internal static class GeneralExtensionMethods
    {
        private static readonly Dictionary<string, MimeType> s_mimeTypeCache = new();

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

        public static bool TryParseAsMimeType(this string mimeRaw, out MimeType mimeType)
        {
            if (s_mimeTypeCache.TryGetValue(mimeRaw, out mimeType))
                return true;

            mimeType = MimeType.None;
            foreach (MimeType t in Enum.GetValues(typeof(MimeType)))
            {
                if (!TryGetDescriptionForEnum(t, out string description))
                    return false;

                if (description.Equals(mimeRaw, StringComparison.Ordinal))
                {
                    mimeType = t;
                    s_mimeTypeCache.Add(mimeRaw, t);
                    return true;
                }
            }

            return false;
        }

        private static bool TryGetDescriptionForEnum(this Enum value, out string description)
        {
            description = string.Empty;
            if (value == null)
                return false;

            Type t = value.GetType();
            string? name = Enum.GetName(t, value);
            if (string.IsNullOrEmpty(name))
                return false;

            FieldInfo? field = t.GetField(name);

            if (field == default)
                return false;

            if (Attribute.GetCustomAttribute(field, typeof(DescriptionAttribute)) is DescriptionAttribute attr)
                description = attr.Description;

            return !string.IsNullOrEmpty(description);
        }
    }
}
