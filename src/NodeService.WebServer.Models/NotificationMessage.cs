using NodeService.Infrastructure.DataModels;
using OneOf;

namespace NodeService.WebServer.Models;

public readonly record struct NotificationMessage
{
    public readonly NotificationConfiguration Configuration;

    public readonly OneOf<EmailContent, LarkContent> Content;

    public NotificationMessage(EmailContent emailContent, NotificationConfiguration configuration)
    {
        Content = emailContent;
        Configuration = configuration;
    }

    public NotificationMessage(LarkContent larkContent, NotificationConfiguration configuration)
    {
        Content = larkContent;
        Configuration = configuration;
    }

}