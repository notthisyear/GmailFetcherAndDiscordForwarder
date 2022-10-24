# GmailFetcherAndDiscordForwarder
A small program that fetches e-mails from a Google Mail account and forwards them to a Discord webhook. Specifically, it targets a Discord forum channel and keeps e-mail threads as responses to single post.

To run the program, a Gmail account where the Google Drive API is enabled is required. Follow the steps under "*Set up your environment*" [at this link](https://developers.google.com/drive/api/quickstart/python) to enable the API and download the necessary `credentials.json` file. 

Next, a webhook must be created at the desired Discord server. Follow the steps [at this link](https://support.discord.com/hc/en-us/articles/228383668-Intro-to-Webhooks) to set it up. Note the `Webhook URL`.

## Running the program
To run the program, simple clone this repository and build it with `dotnet build`. The build command should restore the required NuGet packages automatically.

Typical usage:

```
GmailFetcherAndDiscordForwarder --credentials-path path/to/credentials 
                                --email-address firstname.lastname@example.com 
                                --discord-webhook-url https://discord.com/api/webhook/id/token
```

The program will first fetch all sent and received e-mail from the specified account and build the cache. Then, it will enter an infinite loop where it periodically checks for new e-mails.

Only new e-mails (sent or received) will generate a Discord post. If a new e-mail is part of a thread and that thread is not yet in Discord, the entire thread will be posted.

Paths to cache files can be specified manually and are if omitted automatically created in the user's home folder.

The interval with which to check for new e-mails can also be set manually (`--fetching-interval`) and defaults to every five minutes.

## E-mail support
The program currently supports both plain text and HTML email (i.e. with MIME types `text/plain` and `text/html`). If a plain text version of the e-mail is available, it is preferred. If not, the program will automatically convert the HTML version to a plain text version.

The program will also strip out excessive whitespace and attempt to remove the e-mail history (if any). The history remover is based on a few rules of thumb, so there are surely cases where it fails.

Attachments are ignored.

AMP e-mails (MIME type `text/x-amp-html`) are ignored.

## Discord posting
A Discord post cannot be longer than 2 000 characters, so longer e-mails will automatically be split into multiple posts. The program will try to find "sane" split points (such as newlines), but the algorithm is probably not bullet-proof.
