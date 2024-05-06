using Microsoft.Extensions.Hosting;
using NodeService.WebServer.Data;
using NodeService.WebServer.Models;

namespace NodeService.WebServer.Services.Notifications;

public class NotificationService : BackgroundService
{
    private readonly IDbContextFactory<ApplicationDbContext> _dbContextFactory;
    private readonly ILogger<NotificationService> _logger;
    private readonly IAsyncQueue<NotificationMessage> _notificationMessageQueue;
    private readonly ExceptionCounter _exceptionCounter;

    public NotificationService(
        ExceptionCounter exceptionCounter,
        ILogger<NotificationService> logger,
        IDbContextFactory<ApplicationDbContext> dbContextFactory,
        IAsyncQueue<NotificationMessage> notificationMessageQueue)
    {
        _logger = logger;
        _dbContextFactory = dbContextFactory;
        _notificationMessageQueue = notificationMessageQueue;
        _exceptionCounter = exceptionCounter;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
            try
            {
                var notificationMessage = await _notificationMessageQueue.DeuqueAsync(stoppingToken);
                switch (notificationMessage.Configuration.ConfigurationType)
                {
                    case NotificationConfigurationType.Email:
                        var emailNotificationMessageHandler = new EmailNotificationHandler();
                        await emailNotificationMessageHandler.HandleAsync(notificationMessage);
                        break;
                }

                await using var dbContext = await _dbContextFactory.CreateDbContextAsync();
                dbContext.NotificationRecordsDbSet.Add(new NotificationRecordModel
                {
                    Id = Guid.NewGuid().ToString(),
                    CreationDateTime = DateTime.UtcNow,
                    Name = notificationMessage.Subject ?? string.Empty,
                    Value = notificationMessage.Message
                });
                await dbContext.SaveChangesAsync();
            }

            catch (Exception ex)
            {
                _exceptionCounter.AddOrUpdate(ex);
                _logger.LogError(ex.ToString());
            }
    }
}