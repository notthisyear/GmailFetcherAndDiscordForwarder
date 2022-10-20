using System;
using System.Text;
using System.Globalization;

namespace GoogleMailFetcher
{
    internal record GoogleEmail
    {
        public bool IsValid { get; }

        public string MailId { get; }

        public string From { get; }

        public string ReturnPath { get; }

        public string Subject { get; }

        public DateTime Date { get; }

        public string Content { get; }

        public string? References { get; }

        public string? InReplyTo { get; }

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

        private static bool TryDecodeBody(string bodyRaw, out string body)
        {
            try
            {
                StringBuilder sb = new();
                int numberOfCharacters = bodyRaw.Length;
                int idx = 0;
                while (idx + 4 <= numberOfCharacters)
                {
                    var currentBase64Content = bodyRaw[idx..(idx + 4)].Replace('-', '+').Replace('_', '/');
                    sb.Append(Encoding.UTF8.GetString(Convert.FromBase64String(currentBase64Content)));
                    idx += 4;
                }
                
                if (idx != numberOfCharacters)
                {
                    var currentBase64Content = bodyRaw[idx..numberOfCharacters].Replace('-', '+').Replace('_', '/').PadRight(4, '=');
                    sb.Append(Encoding.UTF8.GetString(Convert.FromBase64String(currentBase64Content)));
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
    }
}
