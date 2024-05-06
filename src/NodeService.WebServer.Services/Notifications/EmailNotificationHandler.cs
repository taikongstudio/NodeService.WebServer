using System.Net.Security;
using System.Security.Cryptography.X509Certificates;
using MailKit;
using MailKit.Net.Smtp;
using MimeKit;
using MimeKit.Text;

namespace NodeService.WebServer.Services.Notifications;

public class EmailNotificationHandler
{
    public async Task<NotificationResult> HandleAsync(NotificationMessage notificationMessage)
    {
        var notificationResult = new NotificationResult();
        if (!notificationMessage.Configuration.Options.TryGetValue(
                NotificationConfigurationType.Email,
                out var emailOptionsValue) || emailOptionsValue is not EmailNotificationOptions options)
        {
            if (emailOptionsValue is JsonElement jsonElement)
                options = jsonElement.Deserialize<EmailNotificationOptions>();
            else
                throw new InvalidOperationException("Email notification is not supported");
        }

        using var smtp = new SmtpClient();
        smtp.MessageSent += MessageSent;
        smtp.ServerCertificateValidationCallback = ServerCertificateValidationCallback;
        await smtp.ConnectAsync(
            options.Host,
            options.Port);
        await smtp.AuthenticateAsync(
            options.UserName,
            options.Password);
        var message = new MimeMessage
        {
            Sender = new MailboxAddress(options.Sender,
                options.UserName),
            Subject = notificationMessage.Subject,
            Body = new TextPart(TextFormat.Plain)
            {
                Text = notificationMessage.Message
            }
        };
        foreach (var to in options.To)
            message.To.Add(new MailboxAddress(to.Name, to.Value));

        foreach (var cc in options.CC)
            message.Cc.Add(new MailboxAddress(cc.Name, cc.Value));

        await smtp.SendAsync(FormatOptions.Default, message);
        await smtp.DisconnectAsync(true);
        return notificationResult;
    }

    private void MessageSent(object sender, MessageSentEventArgs e)
    {
    }

    private bool ServerCertificateValidationCallback(
        object sender,
        X509Certificate? certificate,
        X509Chain? chain,
        SslPolicyErrors sslPolicyErrors)
    {
        return true;
    }
}