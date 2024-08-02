using System.Collections.Immutable;

namespace NodeService.WebServer.Models;

public class EmailContent
{
    public EmailContent(string subject, string body, ImmutableArray<EmailAttachment> attachments)
    {
        Subject = subject;
        Body = body;
        Attachments = attachments;
    }

    public string Body { get; init; }

    public string Subject { get; init; }

    public ImmutableArray<EmailAttachment> Attachments { get; init; }
}
