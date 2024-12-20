﻿using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NodeService.Infrastructure.DataModels;
using NodeService.Infrastructure.Logging;
using NodeService.WebServer.Data.Repositories;
using NodeService.WebServer.Services.DataServices;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace NodeService.WebServer.Services.Tasks
{
    public class TaskLogAnalysisService : BackgroundService
    {
        readonly ILogger<TaskLogAnalysisService> _logger;
        readonly IAsyncQueue<TaskLogUnit> _taskLogUnitQueue;
        readonly ObjectCache _objectCache;
        readonly ExceptionCounter _exceptionCounter;
        readonly ApplicationRepositoryFactory<TaskProgressInfoModel> _taskProgressInfoRepoFactory;
        readonly BatchQueue<TaskProgressEntry> _batchQueue;

        public TaskLogAnalysisService(
            ILogger<TaskLogAnalysisService> logger,
            ExceptionCounter exceptionCounter,
            [FromKeyedServices(nameof(TaskLogAnalysisService))] IAsyncQueue<TaskLogUnit> taskLogUnitQueue,
            ObjectCache objectCache,
            ApplicationRepositoryFactory<TaskProgressInfoModel> taskProgressInfoRepoFactory)
        {
            _logger = logger;
            _taskLogUnitQueue = taskLogUnitQueue;
            _objectCache = objectCache;
            _exceptionCounter = exceptionCounter;
            _taskProgressInfoRepoFactory = taskProgressInfoRepoFactory;
            _batchQueue = new BatchQueue<TaskProgressEntry>();
        }

        protected override async Task ExecuteAsync(CancellationToken cancellationToken = default)
        {
            await Task.WhenAll(
                PersistTaskProgressInfoAsync(cancellationToken),
                AnalysisTaskLogAsync(cancellationToken));
        }
         
        private async Task PersistTaskProgressInfoAsync(CancellationToken cancellationToken = default)
        {
            await foreach (var entries in _batchQueue.ReceiveAllAsync(cancellationToken))
            {
                if (entries == null)
                {
                    continue;
                }
                foreach (var taskProgressEntryGroup in entries.GroupBy(static x => x.Id))
                {
                    var id = taskProgressEntryGroup.Key;
                    if (id == null)
                    {
                        continue;
                    }

                    try
                    {
                        await using var taskProgressInfoRepo = await _taskProgressInfoRepoFactory.CreateRepositoryAsync(cancellationToken);
                        var taskProgressInfo = await taskProgressInfoRepo.GetByIdAsync(id, cancellationToken);
                        var addOrUpdate = false;
                        if (taskProgressInfo == null)
                        {
                            taskProgressInfo = new TaskProgressInfoModel()
                            {
                                Id = id,
                                Name = string.Empty,
                                CreationDateTime = DateTime.UtcNow,
                                ModifiedDateTime = DateTime.UtcNow,
                            };
                            addOrUpdate = true;
                        }
                        var changed = false;
                        foreach (var taskProgressEntry in taskProgressEntryGroup)
                        {
                            if (taskProgressInfo.Value.Entries.Count == 0)
                            {
                                taskProgressInfo.Value.Entries.Add(taskProgressEntry);
                                changed = true;
                            }
                            else
                            {
                                var found = false;
                                for (int i = 0; i < taskProgressInfo.Entries.Count; i++)
                                {
                                    var entry = taskProgressInfo.Entries[i];
                                    if (entry.TaskName == taskProgressEntry.TaskName)
                                    {
                                        if (entry.Progress != taskProgressEntry.Progress)
                                        {
                                            entry.Progress = taskProgressEntry.Progress;
                                            taskProgressInfo.Entries[i] = entry;
                                            changed = true;
                                        }
                                        found = true;
                                        break;
                                    }
                                }
                                if (!found)
                                {
                                    taskProgressInfo.Entries.Add(taskProgressEntry);
                                    changed = true;
                                }
                            }
                        }



                        if (addOrUpdate)
                        {
                            await taskProgressInfoRepo.AddAsync(taskProgressInfo, cancellationToken);
                        }
                        else
                        {
                            if (changed)
                            {
                                await taskProgressInfoRepo.UpdateAsync(taskProgressInfo, cancellationToken);
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex.ToString());
                        _exceptionCounter.AddOrUpdate(ex, id);
                    }

                }

            }
        }

        private async Task AnalysisTaskLogAsync(CancellationToken cancellationToken)
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    var taskLogUnit = await _taskLogUnitQueue.DeuqueAsync(cancellationToken);
                    if (taskLogUnit == null) { continue; }
                    await AnalysisAsync(taskLogUnit, cancellationToken);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex.ToString());
                    _exceptionCounter.AddOrUpdate(ex);
                }

            }
        }

        private async ValueTask AnalysisAsync(TaskLogUnit taskLogUnit, CancellationToken cancellationToken = default)
        {
            foreach (var item in taskLogUnit.LogEntries)
            {
                if (item.Value == null)
                {
                    continue;
                }
                await AnalysisTaskProgressAsync(taskLogUnit, item, cancellationToken);
            }
        }

        private async Task AnalysisTaskProgressAsync(
            TaskLogUnit taskLogUnit,
            LogEntry item,
            CancellationToken cancellationToken)
        {
            try
            {
                if (!TaskLogKafkaProducerServiceHelpers.TryParseTaskProgressEntry(item, out TaskProgressEntry taskProgressEntry))
                {
                    return;
                }
                if (taskProgressEntry == default)
                {
                    return;
                }
                taskProgressEntry.Id = taskLogUnit.Id;
                await _batchQueue.SendAsync(taskProgressEntry, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex.ToString());
                _exceptionCounter.AddOrUpdate(ex, taskLogUnit.Id ?? string.Empty);
            }
        }

    }
}
