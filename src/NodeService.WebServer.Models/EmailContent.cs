using System.Collections.Immutable;

namespace NodeService.WebServer.Models;

public class EmailContent
{
    //public const string DateTimeFormat = "yyyy_MM_dd_HH_mm_ss";
    public const string DateTimeFormat = "yyyy_MM_dd HH:mm:ss";
    //public const string DateTimeFormat = "yyyy/MM/dd HH:mm:ss";
    public EmailContent(string subject, string body, ImmutableArray<EmailAttachmentBase> attachments)
    {
        Subject = subject;
        Body = body;
        Attachments = attachments;
    }

    public string Body { get; init; }

    public string Subject { get; init; }

    public ImmutableArray<EmailAttachmentBase> Attachments { get; init; }
}
