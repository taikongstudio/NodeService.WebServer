using MailKit;
using MailKit.Security;
using MimeKit;
using System.Net.Security;
using System.Security.Cryptography.X509Certificates;

namespace NodeService.WebServer.Services.Notifications
{
    public class EmailNotificationHandler
    {
        public async Task<NotificationResult> HandleAsync(NotificationMessage notificationMessage)
        {
            NotificationResult notificationResult = new NotificationResult();
            try
            {
                using var smtp = new MailKit.Net.Smtp.SmtpClient();
                smtp.MessageSent += MessageSent;
                smtp.ServerCertificateValidationCallback = ServerCertificateValidationCallback;
                await smtp.ConnectAsync(
                    notificationMessage.Configuration.Email.Host,
                    notificationMessage.Configuration.Email.Port,
                    SecureSocketOptions.Auto);
                await smtp.AuthenticateAsync(
                    notificationMessage.Configuration.Email.UserName,
                    notificationMessage.Configuration.Email.Password);
                var message = new MimeMessage
                {
                    Sender = new MailboxAddress(notificationMessage.Configuration.Email.Sender, notificationMessage.Configuration.Email.UserName),
                    Subject = notificationMessage.Subject,
                    Body = new TextPart(MimeKit.Text.TextFormat.Plain)
                    {
                        Text = notificationMessage.Message
                    }
                };
                foreach (var to in notificationMessage.Configuration.Email.To)
                {
                    message.To.Add(new MailboxAddress(to.Name, to.Value));
                }

                foreach (var cc in notificationMessage.Configuration.Email.CC)
                {
                    message.Cc.Add(new MailboxAddress(cc.Name, cc.Value));
                }

                await smtp.SendAsync(MimeKit.FormatOptions.Default, message);
                await smtp.DisconnectAsync(true);
            }
            catch (Exception ex)
            {
                notificationResult.ErrorCode = ex.HResult;
                notificationResult.Message = ex.Message;
            }
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
}
