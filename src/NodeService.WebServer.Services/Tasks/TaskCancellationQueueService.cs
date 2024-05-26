using Microsoft.Extensions.Hosting;
using NodeService.Infrastructure.Models;
using NodeService.Infrastructure.NodeSessions;
using NodeService.WebServer.Data.Repositories;
using NodeService.WebServer.Services.Counters;
using NodeService.WebServer.Services.NodeSessions;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace NodeService.WebServer.Services.Tasks
{
    public class TaskCancellationQueueService : BackgroundService
    {
        readonly ILogger<TaskCancellationQueueService> _logger;
        readonly ExceptionCounter _exceptionCounter;
        readonly ITaskPenddingContextManager _taskContextManager;
        private readonly INodeSessionService _nodeSessionService;
        readonly BatchQueue<TaskCancellationParameters> _taskCancellationBatchQueue;
        private readonly ApplicationRepositoryFactory<JobExecutionInstanceModel> _taskExecutionInstanceRepoFactory;

        public TaskCancellationQueueService(
            ILogger<TaskCancellationQueueService> logger,
            ExceptionCounter exceptionCounter,
            ITaskPenddingContextManager taskContextManager,
            INodeSessionService nodeSessionService,
            BatchQueue<TaskCancellationParameters> taskCancellationBatchQueue,
            ApplicationRepositoryFactory<JobExecutionInstanceModel> taskExecutionInstanceRepoFactory)
        {
            _logger = logger;
            _exceptionCounter = exceptionCounter;
            _taskContextManager = taskContextManager;
            _nodeSessionService = nodeSessionService;
            _taskCancellationBatchQueue = taskCancellationBatchQueue;
            _taskExecutionInstanceRepoFactory = taskExecutionInstanceRepoFactory;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            await foreach (var arrayPoolCollection in _taskCancellationBatchQueue.ReceiveAllAsync(stoppingToken))
            {
                try
                {
                    using var taskExecutionInstanceRepo = _taskExecutionInstanceRepoFactory.CreateRepository();
                    foreach (var taskCancellationParameters in arrayPoolCollection)
                    {
                        if (_taskContextManager.TryGetContext(
                            taskCancellationParameters.TaskExeuctionInstanceId,
                            out var context)
                            &&
                            context != null)
                        {
                            await context.CancelAsync();
                        }
                        var taskExecutionInstance = await taskExecutionInstanceRepo.GetByIdAsync(
                                                        taskCancellationParameters.TaskExeuctionInstanceId,
                                                        stoppingToken);
                        if (taskExecutionInstance == null)
                        {
                            continue;
                        }
                        foreach (var nodeSessionId in _nodeSessionService.EnumNodeSessions(new NodeId(taskExecutionInstance.NodeInfoId)))
                        {
                            await _nodeSessionService.PostTaskExecutionEventAsync(
                                    nodeSessionId,
                                    taskExecutionInstance.ToCancelEvent(),
                                    stoppingToken);
                        }

                    }
                }
                catch (Exception ex)
                {
                    _exceptionCounter.AddOrUpdate(ex);
                    _logger.LogError(ex.ToString());
                }
                finally
                {

                }
            }
        }
    }
}
