using Microsoft.Extensions.Hosting;
using NodeService.WebServer.Data;
using NodeService.WebServer.Models;
using NodeService.WebServer.Services.Counters;

namespace NodeService.WebServer.Services.Notifications;

public class NotificationService : BackgroundService
{
    private readonly IDbContextFactory<ApplicationDbContext> _dbContextFactory;
    private readonly ExceptionCounter _exceptionCounter;
    private readonly WebServerCounter _webServerCounter;
    private readonly ILogger<NotificationService> _logger;
    private readonly IAsyncQueue<NotificationMessage> _notificationMessageQueue;

    public NotificationService(
        WebServerCounter webServerCounter,
        ExceptionCounter exceptionCounter,
        ILogger<NotificationService> logger,
        IDbContextFactory<ApplicationDbContext> dbContextFactory,
        IAsyncQueue<NotificationMessage> notificationMessageQueue)
    {
        _logger = logger;
        _dbContextFactory = dbContextFactory;
        _notificationMessageQueue = notificationMessageQueue;
        _exceptionCounter = exceptionCounter;
        _webServerCounter = webServerCounter;
    }

    protected override async Task ExecuteAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
            try
            {
                var notificationMessage = await _notificationMessageQueue.DeuqueAsync(cancellationToken);
                switch (notificationMessage.Configuration.ConfigurationType)
                {
                    case NotificationConfigurationType.Email:
                        var emailNotificationMessageHandler = new EmailNotificationHandler();
                        await emailNotificationMessageHandler.HandleAsync(notificationMessage);
                        break;
                    case NotificationConfigurationType.Lark:
                        var larkNotificationMessageHandler = new LarkNotificationHandler();
                        await larkNotificationMessageHandler.HandleAsync(notificationMessage);
                        break;
                }

                await using var dbContext = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
                switch (notificationMessage.Content.Index)
                {
                    case 0:
                        dbContext.NotificationRecordDbSet.Add(new NotificationRecordModel
                        {
                            Id = Guid.NewGuid().ToString(),
                            CreationDateTime = DateTime.UtcNow,
                            Name = notificationMessage.Content.AsT0.Subject ?? string.Empty,
                            Value = notificationMessage.Content.AsT0.Body
                        });
                        break;
                    case 1:
                        dbContext.NotificationRecordDbSet.Add(new NotificationRecordModel
                        {
                            Id = Guid.NewGuid().ToString(),
                            CreationDateTime = DateTime.UtcNow,
                            Name = notificationMessage.Content.AsT1.Subject ?? string.Empty,
                            Value = string.Join(Environment.NewLine, notificationMessage.Content.AsT1.Entries.Select(x => $"{x.Name}:{x.Value}"))
                        });
                        break;
                    default:
                        break;
                }


                await dbContext.SaveChangesAsync(cancellationToken);

                _webServerCounter.Snapshot.NotificationRecordCount.Value++;

            }

            catch (Exception ex)
            {
                _exceptionCounter.AddOrUpdate(ex);
                _logger.LogError(ex.ToString());
            }
    }

}