using Microsoft.Extensions.Hosting;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace NodeService.WebServer.Services.Notifications
{
    public class NotificationService : BackgroundService
    {
        private readonly IAsyncQueue<NotificationMessage> _notificationMessageQueue;

        public NotificationService(IAsyncQueue<NotificationMessage> notificationMessageQueue)
        {
            _notificationMessageQueue = notificationMessageQueue;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                var notificationMessage = await this._notificationMessageQueue.DeuqueAsync(stoppingToken);
                switch (notificationMessage.Configuration.ConfigurationType)
                {
                    case NotificationConfigurationType.Email:
                        EmailNotificationHandler emailNotificationMessageHandler = new EmailNotificationHandler();
                        await emailNotificationMessageHandler.HandleAsync(notificationMessage);
                        break;
                    default:
                        break;
                }
            }
        }
    }
}
