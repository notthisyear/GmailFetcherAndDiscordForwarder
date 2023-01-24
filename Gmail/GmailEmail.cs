using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Globalization;
using System.ComponentModel;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using HtmlAgilityPack;
using GmailFetcherAndDiscordForwarder.Common;

namespace GmailFetcherAndDiscordForwarder.Gmail
{
    internal enum MailType
    {
        NotSet,
        Sent,
        Received
    }

    internal enum MimeType
    {
        [Description("N/a")]
        None,

        [Description("text/plain")]
        TextPlain,

        [Description("text/html")]
        TextHtml,

        [Description("text/x-amp-html")]
        TextAmpHtml
    }

    internal record GmailEmail
    {
        #region Public methods
        public bool IsValid { get; }

        public string MailId { get; }

        public string MessageId { get; }

        public string From { get; }

        public string? To { get; }

        public string Subject { get; }

        public DateTime Date { get; }

        [JsonConverter(typeof(StringEnumConverter))]
        public MailType MailType { get; }

        public string? ReturnPath { get; }

        public string? InReplyTo { get; }
        #endregion

        private static readonly Regex s_filterKnownDateVariants = new(@"((\(UTC\))|(\(GMT\))|(\(PDT\))|(\(CEST\))|(\(CET\)))", RegexOptions.Compiled);

        [JsonProperty]
        private readonly List<(MimeType type, string value)> _content;

        private GmailEmail(MailType mailType)
        {
            IsValid = false;
            MailType = mailType;
            From = string.Empty;
            ReturnPath = string.Empty;
            Subject = string.Empty;
            Date = DateTime.MinValue;
            MailId = string.Empty;
            MessageId = string.Empty;

            _content = new();
        }

        [JsonConstructor]
        private GmailEmail(MailType mailType, string mailId, string messageId, string from, string subject, DateTime date, List<(MimeType type, string value)> content, string? to, string? references, string? inReplyTo)
        {
            IsValid = true;
            MailType = mailType;

            MailId = mailId;
            MessageId = messageId;
            From = from;
            To = to;
            Subject = subject;
            Date = date;
            InReplyTo = inReplyTo;

            _content = content;
        }

        #region Public methods

        public static GmailEmail ConstructEmpty(MailType type)
            => new(type);

        public static GmailEmail Construct(MailType type, string id, string? from, string? to, string? date, string? messageId, string? subject, string? returnPath, string? inReplyTo, List<(MimeType type, string content)> bodyPartsRaw)
        {
            if (type == MailType.NotSet)
                return new(type);

            if (string.IsNullOrEmpty(from))
                return new(type);

            if (string.IsNullOrEmpty(to) && type == MailType.Sent)
                return new(type);

            if (string.IsNullOrEmpty(date))
                return new(type);

            if (string.IsNullOrEmpty(messageId))
                return new(type);

            if (!DateTime.TryParse(date, CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime d))
            {
                // There are some variants of the date string that the TryParse cannot always pick up automatically
                // We check for some known cases and try again
                date = s_filterKnownDateVariants.Replace(date, string.Empty);
                if (!DateTime.TryParse(date, CultureInfo.InvariantCulture, DateTimeStyles.None, out d))
                    return new(type);
            }

            List<(MimeType type, string content)> bodyParts = new();
            foreach (var part in bodyPartsRaw)
            {
                if (!TryDecodeBody(part.content, out var body))
                    LoggerType.Internal.Log(LoggingLevel.Warning, $"Failed to decode raw body part with MIME type '{part.type}'");
                else
                    bodyParts.Add((part.type, body));
            }

            if (!bodyParts.Any())
                return new(type);

            return new GmailEmail(type, id, messageId, from, subject ?? string.Empty, d, bodyParts, to, returnPath, inReplyTo);
        }

        public string GetAsContentAsPlainText(bool tryRemoveHistory)
        {
            string result = string.Empty;
            if (!_content.Any())
                result = string.Empty;
            else if (_content.Select(x => x.type).Contains(MimeType.TextPlain))
                result = _content.First(x => x.type == MimeType.TextPlain).value;
            else if (_content.Select(x => x.type).Contains(MimeType.TextHtml))
                result = ConvertHtmlToPlainText(_content.First(x => x.type == MimeType.TextHtml).value, tryRemoveHistory);
            else if (_content.Select(x => x.type).Contains(MimeType.TextAmpHtml))
                LoggerType.Internal.Log(LoggingLevel.Warning, $"Cannot generate plain text from e-mail '{MessageId}' - cannot parse MIME type '{MimeType.TextAmpHtml}' is available");

            return TrimContent(result, tryRemoveHistory);
        }
        #endregion

        #region Private methods
        private static bool TryDecodeBody(string bodyRaw, out string body)
        {
            // Note: This approach allow us to decode as much as possible, rather than returning nothing if there is some invalid
            // characters in the raw body
            StringBuilder sb = new();
            try
            {
                var numberOfCharacters = bodyRaw.Length;
                var idx = 0;
                List<byte> currentData = new();
                while (idx + 4 <= numberOfCharacters)
                {
                    byte[] currentSlice;
                    currentData.Clear();
                    do
                    {
                        currentSlice = Convert.FromBase64String(GetDecodeableBase64SliceFromRawText(bodyRaw, idx));
                        idx += 4;
                        for (var i = 0; i < currentSlice.Length; i++)
                            currentData.Add(currentSlice[i]);

                    } while (ContainsUnfinishedMultiByteCharacter(currentSlice));

                    sb.Append(Encoding.UTF8.GetString(currentData.ToArray()));
                }

                body = sb.ToString();
            }
            catch (Exception e) when (e is ArgumentException || e is DecoderFallbackException || e is FormatException)
            {
                body = sb.ToString();
            }
            return !string.IsNullOrEmpty(body);
        }

        private static string GetDecodeableBase64SliceFromRawText(string text, int startIdx)
        {
            var pad = startIdx + 4 >= text.Length;
            var endIdx = pad ? text.Length : startIdx + 4;
            var slice = text[startIdx..endIdx];
            ReplaceRFC4648Characters(ref slice);
            return pad ? slice.PadLeft(4, '=') : slice;
        }

        // E-mail bodies use a variant of Base-64 encoding with filename safe characters (https://datatracker.ietf.org/doc/html/rfc4648#section-5)
        private static void ReplaceRFC4648Characters(ref string s)
            => s = s.Replace('-', '+').Replace('_', '/');

        private static bool ContainsUnfinishedMultiByteCharacter(byte[] currentData)
        {
            /* Multi-byte UTF-8 encodings:
            *
            *   # bytes     Byte 1      Byte 2      Byte 3      Byte 4
            *   1           0xxx xxxx
            *   2           110x xxxx   10xx xxxx
            *   3           1110 xxxx   10xx xxxx   10xx xxxx
            *   4           1111 0xxx   10xx xxxx   10xx xxxx   10xx xxxx
            */

            var byteIdx = 0;
            while (byteIdx < currentData.Length)
            {
                var data = currentData[byteIdx];
                int i;
                for (i = 0; i < 5; i++)
                {
                    if ((data >> 7 - i & 0x01) == 0x00)
                        break;
                }

                // Single-byte codepoint
                if (i == 0)
                {
                    byteIdx++;
                    continue;
                }

                // Multi-byte codepoint
                // i is now the total number of bytes

                // There is enough data to encompass the entire codepoint
                if (byteIdx + (i - 1) < currentData.Length)
                    byteIdx += i;
                else
                    return true;
            }

            return false;
        }

        public static string ConvertHtmlToPlainText(string value, bool tryRemoveHistory)
        {
            using StringReader r = new(value);
            var currentContent = new HtmlDocument();
            currentContent.Load(r);

            if (currentContent.ParseErrors.Any())
            {
                int numberOfErrors = currentContent.ParseErrors.Count();
                var firstError = currentContent.ParseErrors.First();
                string parseErrorText = $"a {firstError.Code} error at {firstError.Line}:{firstError.LinePosition} - {firstError.Reason} ({firstError.Code})";
                LoggerType.Internal.Log(LoggingLevel.Warning, $"Parsing of HTML e-mail failed with {numberOfErrors} {(numberOfErrors == 1 ? "error" : "errors")} - first error: {parseErrorText}");
                return string.Empty;
            }

            if (!currentContent.DocumentNode.HasChildNodes)
            {
                LoggerType.Internal.Log(LoggingLevel.Warning, "Parsing of HTML e-mail failed, could not find any nodes");
                return string.Empty;
            }

            // Probably "<html><head><body></body></head></html>"-structure
            StringBuilder sb = new();
            var htmlNode = currentContent.DocumentNode.ChildNodes.FirstOrDefault(x => x.Name.Equals("html", StringComparison.OrdinalIgnoreCase));
            if (htmlNode != default)
            {
                if (htmlNode.HasChildNodes)
                {
                    var bodyNode = htmlNode.ChildNodes.FirstOrDefault(x => x.Name.Equals("body", StringComparison.OrdinalIgnoreCase));
                    if (bodyNode != default)
                    {
                        BuildContentFromBodyNodes(bodyNode, tryRemoveHistory, sb);
                        return sb.ToString();
                    }
                    LoggerType.Internal.Log(LoggingLevel.Warning, "Parsing of HTML e-mail failed, could not find body node");
                    return string.Empty;
                }
                LoggerType.Internal.Log(LoggingLevel.Warning, "Parsing of HTML e-mail failed, HTML node does not have any children");
                return string.Empty;
            }

            // The body can be omitted 
            foreach (var node in currentContent.DocumentNode.ChildNodes)
            {
                if (node.Name.Equals("meta", StringComparison.OrdinalIgnoreCase))
                    continue;

                BuildContentFromBodyNodes(node, tryRemoveHistory, sb);
            }

            return sb.ToString();
        }

        private static void BuildContentFromBodyNodes(HtmlNode bodyNode, bool tryRemoveHistory, StringBuilder contentBuilder)
        {
            foreach (var node in bodyNode.ChildNodes)
            {
                if (tryRemoveHistory)
                {
                    if (node.Name.Equals("blockquote", StringComparison.OrdinalIgnoreCase))
                        continue;
                }

                if (node.Name.Equals("#text", StringComparison.OrdinalIgnoreCase))
                    contentBuilder.Append(node.InnerText.Replace("&nbsp;", " "));
                else if (node.Name.Equals("br", StringComparison.OrdinalIgnoreCase))
                    contentBuilder.AppendLine();
                else if (node.Name.Equals("p", StringComparison.OrdinalIgnoreCase) && node.InnerText.Equals("&nbsp;", StringComparison.OrdinalIgnoreCase))
                    contentBuilder.AppendLine();
                else if (node.HasChildNodes)
                    BuildContentFromBodyNodes(node, tryRemoveHistory, contentBuilder);
            }
        }

        private static string TrimContent(string content, bool tryRemoveHistory)
        {
            if (string.IsNullOrEmpty(content))
                return string.Empty;

            StringBuilder sb = new();
            int currentIdx = 0;
            int lastInsertIndex = 0;
            int lastInsertLength = 0;
            bool lastLineWasHistory = false;
            bool lastInsertedLineWasEmpty = false;

            while (currentIdx < content.Length)
            {
                // Email use CRLF (https://www.rfc-editor.org/rfc/rfc5322, section 2.3)
                int newLineIndex = content[currentIdx..].IndexOf("\n");
                int indexOfNextNewline = newLineIndex == -1 ? content.Length : newLineIndex + currentIdx;
                string currentLine = content[currentIdx..indexOfNextNewline];

                // Empty line
                if (currentLine.Trim().Length == 0)
                {
                    if (lastInsertedLineWasEmpty)
                    {
                        currentIdx = indexOfNextNewline + 1;
                        continue;
                    }

                    lastInsertedLineWasEmpty = true;
                    sb.AppendLine();
                }
                else
                {
                    if (tryRemoveHistory && content[currentIdx] == '>')
                    {
                        // First history line is typically preceded by "at date someone wrote..."
                        if (!lastLineWasHistory && !lastInsertedLineWasEmpty)
                            sb.Remove(lastInsertIndex, lastInsertLength);

                        lastLineWasHistory = true;
                    }
                    else
                    {
                        string lineToAppend = currentLine.TrimEnd().Replace("&nbsp;", " ");
                        sb.AppendLine(lineToAppend);

                        lastInsertedLineWasEmpty = false;
                        lastLineWasHistory = false;
                        lastInsertLength = lineToAppend.Length;
                        lastInsertIndex = sb.Length - lastInsertLength;
                    }
                }

                currentIdx = indexOfNextNewline + 1;
            }
            return sb.ToString();
        }
        #endregion
    }
}
