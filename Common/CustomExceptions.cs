using System;
using System.Runtime.Serialization;

namespace GmailFetcherAndDiscordForwarder.Common
{
    internal class GmailCommunicationException : Exception
    {
        public GmailCommunicationException() { }
        public GmailCommunicationException(string? message) : base(message) { }
        public GmailCommunicationException(string? message, Exception? innerException) : base(message, innerException) { }
        protected GmailCommunicationException(SerializationInfo info, StreamingContext context) : base(info, context) { }

    }
    internal class InvalidGmailResponseException : Exception
    {
        public InvalidGmailResponseException() { }
        public InvalidGmailResponseException(string? message) : base(message) { }
        public InvalidGmailResponseException(string? message, Exception? innerException) : base(message, innerException) { }
        protected InvalidGmailResponseException(SerializationInfo info, StreamingContext context) : base(info, context) { }
    }
}
