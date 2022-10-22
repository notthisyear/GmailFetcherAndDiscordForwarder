using System;
using System.Net.Sockets;
using GmailFetcherAndForwarder.Gmail;

namespace GmailFetcherAndForwarder.Common
{
    internal static class GeneralExtensionMethods
    {
        public static string FormatException(this Exception e)
        {
            if (e is SocketException socketException)
                return $"{socketException.GetType()} (socket error code: {socketException.ErrorCode}): {e.Message}";
            else
                return $"{e.GetType()}: {e.Message}";
        }

        public static string AsString(this MailType type)
            => $"{type.ToString().ToLower()}";
    }
}
