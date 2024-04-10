using FluentFTP.Helpers;
using Microsoft.Extensions.Hosting;
using NodeService.WebServer.Data;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace NodeService.WebServer.Services.Notifications
{
    public class NotificationService : BackgroundService
    {
        private readonly ILogger<NotificationService> _logger;
        private readonly IDbContextFactory<ApplicationDbContext> _dbContextFactory;
        private readonly IAsyncQueue<NotificationMessage> _notificationMessageQueue;

        public NotificationService(
            ILogger<NotificationService> logger,
            IDbContextFactory<ApplicationDbContext> dbContextFactory,
            IAsyncQueue<NotificationMessage> notificationMessageQueue)
        {
            _logger = logger;
            _dbContextFactory = dbContextFactory;
            _notificationMessageQueue = notificationMessageQueue;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            while (!stoppingToken.IsCancellationRequested)
            {

                try
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
                    using var dbContext = _dbContextFactory.CreateDbContext();
                    dbContext.NotificationRecordsDbSet.Add(new NotificationRecordModel()
                    {
                        Id = Guid.NewGuid().ToString(),
                        CreationDateTime = DateTime.UtcNow,
                        Name = notificationMessage.Subject ?? string.Empty,
                        Value = notificationMessage.Message,
                    });
                    await dbContext.SaveChangesAsync();
                }

                catch (Exception ex)
                {
                    _logger.LogError(ex.ToString());
                }

            }
        }
    }
}
