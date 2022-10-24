using System;
using System.Linq;
using System.Collections.Generic;
using Xunit;
using GmailFetcherAndDiscordForwarder.Gmail;

namespace GmailFetcherAndDiscordForwarderTests
{
    public class EmailDecoderTests
    {
        public enum MailPart
        {
            MailId,
            From,
            To,
            Date,
            MessageId,
            Subject,
            ReturnPath,
            InReplyTo,
            BodyRaw
        }

        private static readonly Dictionary<MailPart, string> s_validDefaultPart = new()
        {
            { MailPart.MailId, "123" },
            { MailPart.From, "a.b@mail.com" },
            { MailPart.To, "c.d@mail.com" },
            { MailPart.Date, "Fri, 21 Oct 2022 13:50:42 +0200" },
            { MailPart.MessageId, "456" },
            { MailPart.Subject, "Test" },
            { MailPart.ReturnPath, "c.d@gmail.com" },
            { MailPart.InReplyTo, "7890" },
            { MailPart.BodyRaw, "TWFueSBoYW5kcyBtYWtlIGxpZ2h0IHdvcmsu" }
        };

        [Fact]
        internal void InvalidMailTypeShouldProduceEmptyEmail()
        {
            Assert.False(GetValidPlainTextEmailWithParts(MailType.NotSet).IsValid);
        }

        [Theory]
        [InlineData(MailPart.From)]
        [InlineData(MailPart.To)]
        [InlineData(MailPart.Date)]
        [InlineData(MailPart.MessageId)]
        [InlineData(MailPart.BodyRaw)]
        internal void MissingDataShouldProduceInvalidEmail(MailPart part)
        {
            Assert.False(GetValidPlainTextEmailWithParts(MailType.Sent, (part, string.Empty)).IsValid);
        }

        [Theory]
        [InlineData("Mon, 16 Jan 2011 15:07:55")]
        internal void InvalidDateShouldProduceEmptyEmail(string date)
        {
            Assert.False(GetValidPlainTextEmailWithParts(MailType.Received, (MailPart.Date, date)).IsValid);
        }

        [Theory]
        [InlineData("a6!h")]
        [InlineData("")]
        internal void InvalidBodyRawShouldProduceEmptyEmail(string bodyRaw)
        {
            Assert.False(GetValidPlainTextEmailWithParts(MailType.Received, (MailPart.BodyRaw, bodyRaw)).IsValid);
        }

        [Theory]
        [InlineData("TWFueSBoYW5kcyBtYWtlIGxpZ2h0IHdvcmsu", "Many hands make light work.")]
        [InlineData("RXR0IGV4ZW1wZWwgbWVkIMOlLCDDpCBvY2ggw7Yu", "Ett exempel med å, ä och ö.")]
        internal void VerifyBodyRawParser(string bodyRaw, string expectedContent)
        {
            var email = GetValidPlainTextEmailWithParts(MailType.Received, (MailPart.BodyRaw, bodyRaw));
            Assert.Equal(expectedContent, email.GetAsContentAsPlainText(true));
        }

        [Theory]
        [InlineData("Fri, 21 Oct 2022 13:50:42 +0200", 2022, 10, 21, 13, 50, 42, 2)]
        [InlineData("Sat, 23 Jun 2018 08:10:44 +0000 (GMT)", 2018, 6, 23, 8, 10, 44, 0)]
        [InlineData("Tue, 8 Mar 2016 22:00:04 +0000 (UTC)", 2016, 3, 8, 22, 0, 4, 0)]
        [InlineData("Thu, 9 Dec 2021 17:22:19 -0700 (PDT)", 2021, 12, 9, 17, 22, 19, -7)]
        [InlineData("Sun, 16 Jan 2011 02:32:29 +0200 (CEST)", 2011, 1, 16, 2, 32, 29, 2)]
        internal void VerifyDateParser(string date, int year, int month, int day, int hour, int minute, int second, double timezoneShift)
        {
            DateTime expected = new(year, month, day, hour, minute, second, DateTimeKind.Utc);
            expected = expected.AddHours(-timezoneShift);
            var email = GetValidPlainTextEmailWithParts(MailType.Received, (MailPart.Date, date));
            Assert.Equal(expected, email.Date.ToUniversalTime());
        }

        private static GmailEmail GetValidPlainTextEmailWithParts(MailType type, params (MailPart part, string value)[]? content)
        {
            List<MailPart> givenParts;
            Dictionary<MailPart, string> parts = new();
            if (content == null || content.Length == 0)
                givenParts = new();
            else
                givenParts = content.Select(x => x.part).ToList();

            foreach (MailPart part in Enum.GetValues(typeof(MailPart)))
            {
                if (givenParts.Contains(part))
                    parts.Add(part, content!.First(x => x.part == part).value);
                else
                    parts.Add(part, s_validDefaultPart[part]);
            }

            return GmailEmail.Construct(type,
                parts[MailPart.MailId],
                parts[MailPart.From],
                parts[MailPart.To],
                parts[MailPart.Date],
                parts[MailPart.MessageId],
                parts[MailPart.Subject],
                parts[MailPart.ReturnPath],
                parts[MailPart.InReplyTo],
                new() { (MimeType.TextPlain, parts[MailPart.BodyRaw]) });
        }
    }
}
