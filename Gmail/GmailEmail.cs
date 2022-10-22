using System;
using System.Text;
using System.Globalization;
using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using System.Text.RegularExpressions;

namespace GmailFetcherAndForwarder.Gmail
{
    internal enum MailType
    {
        NotSet,
        Sent,
        Received
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

        public string Content { get; }

        [JsonConverter(typeof(StringEnumConverter))]
        public MailType MailType { get; }

        public string? ReturnPath { get; }

        public string? InReplyTo { get; }
        #endregion


        private static readonly Regex s_filterKnownDateVariants = new(@"((\(UTC\))|(\(GMT\))|(\(PDT\))|(\(CEST\)))", RegexOptions.Compiled);

        private GmailEmail(MailType mailType)
        {
            IsValid = false;
            MailType = mailType;
            From = string.Empty;
            ReturnPath = string.Empty;
            Subject = string.Empty;
            Date = DateTime.MinValue;
            Content = string.Empty;
            MailId = string.Empty;
            MessageId = string.Empty;
        }

        [JsonConstructor]
        private GmailEmail(MailType mailType, string mailId, string messageId, string from, string subject, DateTime date, string content, string? to, string? references, string? inReplyTo)
        {
            IsValid = true;
            MailType = mailType;

            MailId = mailId;
            MessageId = messageId;
            From = from;
            To = to;
            Subject = subject;
            Date = date;
            Content = content;
            InReplyTo = inReplyTo;
        }

        public static GmailEmail ConstructEmpty(MailType type)
            => new(type);

        public static GmailEmail Construct(MailType type, string id, string? from, string? to, string? date, string? messageId, string? subject, string? returnPath, string? inReplyTo, string bodyRaw)
        {
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

            if (!TryDecodeBody(bodyRaw, out var body))
                return new(type);

            return new GmailEmail(type, id, messageId, from, subject ?? string.Empty, d, body, to, returnPath, inReplyTo);
        }

        #region Private methods
        private static bool TryDecodeBody(string bodyRaw, out string body)
        {
            // Note: This approach allow us to decode as much as possible, rather than returning nothing if there is some invalid
            // characters in the raw body
            try
            {
                StringBuilder sb = new();
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
                return true;
            }
            catch (Exception e) when (e is ArgumentException || e is DecoderFallbackException)
            {
                body = string.Empty;
                return false;
            }
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

        internal string GetContentFormatted()
        {
            throw new NotImplementedException();
        }
        #endregion
    }
}
