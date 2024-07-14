using Microsoft.Extensions.Hosting;
using NodeService.WebServer.Data;
using NodeService.WebServer.Data.Repositories;
using NodeService.WebServer.Data.Repositories.Specifications;
using NodeService.WebServer.Services.Counters;
using System;
using System.Collections.Generic;
using System.Configuration;
using System.Linq;
using System.Text;
using System.Text.Encodings.Web;
using System.Threading.Tasks;

namespace NodeService.WebServer.Services.DataQuality;

public class DataQualityAlarmService : BackgroundService
{
    private readonly ILogger<DataQualityAlarmService> _logger;
    private readonly ExceptionCounter _exceptionCounter;
    private readonly IAsyncQueue<NotificationMessage> _notificationQueue;
    private readonly BatchQueue<DataQualityAlarmMessage> _alarmMessageBatchQueue;
    private readonly ApplicationRepositoryFactory<NotificationConfigModel> _notificationRepositoryFactory;
    private readonly ApplicationRepositoryFactory<PropertyBag> _propertyBagRepoFactory;

    public DataQualityAlarmService(
        ILogger<DataQualityAlarmService> logger,
        ExceptionCounter exceptionCounter,
        IAsyncQueue<NotificationMessage> notificationQueue,
        BatchQueue<DataQualityAlarmMessage> alarmMessageBatchQueue,
        ApplicationRepositoryFactory<NotificationConfigModel> notificationRepoFactory,
        ApplicationRepositoryFactory<PropertyBag> propertyBagRepoFactory)
    {
        _logger = logger;
        _exceptionCounter = exceptionCounter;
        _notificationQueue = notificationQueue;
        _alarmMessageBatchQueue = alarmMessageBatchQueue;
        _notificationRepositoryFactory = notificationRepoFactory;
        _propertyBagRepoFactory = propertyBagRepoFactory;
    }

    protected override async Task ExecuteAsync(CancellationToken cancellationToken)
    {
        await foreach (var arrayPoolCollection in _alarmMessageBatchQueue.ReceiveAllAsync(cancellationToken))
        {
            try
            {
                await using var propertyBagRepo = await _propertyBagRepoFactory.CreateRepositoryAsync();
                var propertyBag = await propertyBagRepo.FirstOrDefaultAsync(
                    new PropertyBagSpecification(NotificationSources.DataQualityCheck));
                DataQualityCheckConfiguration? configuration;

                while (true)
                {
                    if (!TryReadConfiguration(propertyBag, out configuration) || configuration == null ||
                        !configuration.IsEnabled)
                        continue;
                    if (TimeOnly.FromDateTime(DateTime.Now) >= configuration.NotificationTime) break;
                    await Task.Delay(TimeSpan.FromMinutes(1), cancellationToken);
                }

                if (configuration == null) continue;
                if (_alarmMessageBatchQueue.TriggerBatchPeriod.TotalMinutes != configuration.NotificationDuration)
                    _alarmMessageBatchQueue.SetTriggerBatchPeriod(
                        TimeSpan.FromMinutes(configuration.NotificationDuration));
                await using var notificationConfigRepo = await _notificationRepositoryFactory.CreateRepositoryAsync();

                var stringBuilder = new StringBuilder();
                foreach (var item in arrayPoolCollection.Where(static x => x != null).OrderBy(static x => x.DateTime))
                {
                    if (string.IsNullOrEmpty(item.Message)) item.Message = "<未知原因>";
                    stringBuilder.Append(
                        $"<tr><td>{item.DateTime}</td><td>{item.MachineName}</td><td>{item.DataSource}</td><td>{item.Message}</td></tr>");
                }

                var content = configuration.ContentFormat.Replace("{0}", stringBuilder.ToString());

                foreach (var entry in configuration.Configurations)
                {
                    if (entry.Value == null) continue;
                    var notificationConfig = await notificationConfigRepo.GetByIdAsync(entry.Value, cancellationToken);
                    if (notificationConfig == null || !notificationConfig.IsEnabled)
                        continue;

                    await _notificationQueue.EnqueueAsync(
                        new NotificationMessage(configuration.Subject,
                            content,
                            notificationConfig.Value),
                        cancellationToken);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex.ToString());
                _exceptionCounter.AddOrUpdate(ex);
            }
            finally
            {
            }
        }

    }

    private bool TryReadConfiguration(
        Dictionary<string, object?>? notificationSourceDictionary,
        out DataQualityCheckConfiguration? configuration)
    {
        configuration = null;
        try
        {
            if (notificationSourceDictionary == null
                ||
                !notificationSourceDictionary.TryGetValue("Value", out var value)
                || value is not string json
               )
                return false;
            configuration = JsonSerializer.Deserialize<DataQualityCheckConfiguration>(json);
        }
        catch (Exception ex)
        {
            _exceptionCounter.AddOrUpdate(ex);
            _logger.LogError(ex.ToString());
        }

        return configuration != null;
    }
}