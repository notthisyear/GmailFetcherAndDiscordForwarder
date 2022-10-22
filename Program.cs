using System;
using System.IO;
using System.Linq;
using System.Net.Mail;
using System.Threading.Tasks;
using System.Collections.Generic;
using CommandLine;
using CommandLine.Text;
using GmailFetcherAndDiscordForwarder.Common;

namespace GmailFetcherAndDiscordForwarder
{
    public class Program
    {
        private const string HelpHeading = "GmailFetcherAndDiscordForwarder - Fetches e-mails from a Google Mail account";
        private const string HelpCopyright = "Copyright (C) 2022 Calle Lindquist";

        public static void Main(string[] args)
        {
            Parser parser = new(x =>
            {
                x.HelpWriter = null;
                x.AutoHelp = true;
                x.AutoVersion = true;
            });

            var result = parser.ParseArguments<GmailFetcherAndDiscordForwarderArguments>(args);
            result.WithParsed(RunProgram)
                  .WithNotParsed(err => RunErrorFlow(result, err));
        }

        private static void RunProgram(GmailFetcherAndDiscordForwarderArguments args)
        {
            if (!File.Exists(args.CredentialsPath))
            {
                LoggerType.Internal.Log(LoggingLevel.Error, $"ERROR: File '{args.CredentialsPath}' cannot be found!");
                Environment.Exit(1);
            }

            if (string.IsNullOrEmpty(args.EmailAddress) || !MailAddress.TryCreate(args.EmailAddress, out _))
            {
                LoggerType.Internal.Log(LoggingLevel.Error, "ERROR: Given mail address could not be parsed as valid address");
                Environment.Exit(1);
            }

            if (string.IsNullOrEmpty(args.DiscordWebhookUri))
            {
                LoggerType.Internal.Log(LoggingLevel.Error, "ERROR: No Discord webhook given");
                Environment.Exit(1);
            }

            var tcs = new TaskCompletionSource();
            Task.Run(async () =>
            {
                await EntryPoint.StartProgram(args);
                tcs.SetResult();
            }).ConfigureAwait(false);

            tcs.Task.Wait();
        }

        private static void RunErrorFlow(ParserResult<GmailFetcherAndDiscordForwarderArguments> result, IEnumerable<Error> errors)
        {
            var isVersionRequest = errors.FirstOrDefault(x => x.Tag == ErrorType.VersionRequestedError) != default;
            var isHelpRequest = errors.FirstOrDefault(x => x.Tag == ErrorType.HelpRequestedError) != default ||
                                errors.FirstOrDefault(x => x.Tag == ErrorType.HelpVerbRequestedError) != default;

            var output = string.Empty;
            if (isHelpRequest)
            {
                output = HelpText.AutoBuild(result,
                h =>
                {
                    h.Heading = HelpHeading;
                    h.Copyright = HelpCopyright;
                    return h;
                });
            }
            else if (isVersionRequest)
            {
                output = "v. 0.1";
            }
            else
            {
                output = errors.Count() > 1 ? "ERRORS:\n" : "ERROR:\n";
                foreach (Error error in errors)
                    output += '\t' + GetErrorText(error) + '\n';
            }
            Console.WriteLine(output);
        }

        private static string GetErrorText(Error error)
        {
            return error switch
            {
                MissingValueOptionError missingValueError => $"Value for argument '{missingValueError.NameInfo.NameText}' is missing",
                UnknownOptionError unknownOptionError => $"Argument '{unknownOptionError.Token}' is unknown",
                MissingRequiredOptionError _ => $"A required option is missing value",
                SetValueExceptionError setValueExceptionError => $"Could not set value for argument '{setValueExceptionError.NameInfo.NameText}': {setValueExceptionError.Exception.Message}",
                BadFormatConversionError badFormatConversionError => $"Argument '{badFormatConversionError.NameInfo.NameText}' has bad format",
                _ => $"Argument parsing failed: '{error.Tag}'"
            };
        }
    }
}
