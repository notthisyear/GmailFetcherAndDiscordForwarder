using System;
using System.Text;
using System.Globalization;
using System.Collections.Generic;

namespace GoogleMailFetcher
{
    internal record GoogleEmail
    {
        #region Public methods
        public bool IsValid { get; }

        public string MailId { get; }

        public string From { get; }

        public string ReturnPath { get; }

        public string Subject { get; }

        public DateTime Date { get; }

        public string Content { get; }

        public string? References { get; }

        public string? InReplyTo { get; }
        #endregion

        private GoogleEmail()
        {
            IsValid = false;
            From = string.Empty;
            ReturnPath = string.Empty;
            Subject = string.Empty;
            Date = DateTime.MinValue;
            Content = string.Empty;
            MailId = string.Empty;
        }

        private GoogleEmail(string mailId, string from, string returnPath, string subject, DateTime dateTime, string content, string? references, string? inReplyTo)
        {
            IsValid = true;
            MailId = mailId;
            From = from;
            ReturnPath = returnPath;
            Subject = subject;
            Date = dateTime;
            Content = content;
            References = references;
            InReplyTo = inReplyTo;
        }

        public static GoogleEmail ConstructEmpty()
            => new();

        public static GoogleEmail Construct(string id, string? from, string? date, string? subject, string? returnPath, string? references, string? inReplyTo, string bodyRaw)
        {
            if (string.IsNullOrEmpty(from))
                return new();

            if (string.IsNullOrEmpty(subject))
                return new();

            if (string.IsNullOrEmpty(date))
                return new();

            if (string.IsNullOrEmpty(returnPath))
                return new();

            if (!DateTime.TryParse(date, CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime d))
                return new();

            if (!TryDecodeBody(bodyRaw, out string body))
                return new();

            return new GoogleEmail(id, from, returnPath, subject, d, body, references, inReplyTo);
        }

        #region Private methods
        private static bool TryDecodeBody(string bodyRaw, out string body)
        {
            try
            {
                StringBuilder sb = new();
                int numberOfCharacters = bodyRaw.Length;
                int idx = 0;
                List<byte> currentData = new();
                while (idx + 4 <= numberOfCharacters)
                {
                    byte[] currentSlice;
                    currentData.Clear();
                    do
                    {
                        currentSlice = Convert.FromBase64String(GetDecodeableBase64SliceFromRawText(bodyRaw, idx));
                        idx += 4;
                        for (int i = 0; i < currentSlice.Length; i++)
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
            bool pad = startIdx + 4 >= text.Length;
            int endIdx = pad ? text.Length : startIdx + 4;
            string slice = text[startIdx..endIdx];
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
                    if ((data >> (7 - i) & 0x01) == 0x00)
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
        #endregion
    }
}
