using Microsoft.Extensions.DependencyInjection;
using NodeService.Infrastructure.Concurrent;
using NodeService.Infrastructure.NodeSessions;
using System;
using System.Net;

namespace NodeService.WebServer.Services.Tasks;

public class TaskExecutionTimeLimitJob : JobBase
{
    private readonly BatchQueue<TaskCancellationParameters> _batchQueue;

    public TaskExecutionTimeLimitJob(IServiceProvider serviceProvider) : base(serviceProvider)
    {
        Logger = serviceProvider.GetService<ILogger<FireTaskJob>>();
        _batchQueue = serviceProvider.GetService<BatchQueue<TaskCancellationParameters>>();
    }

    public override async Task Execute(IJobExecutionContext context)
    {
        var nodeSessionService = ServiceProvider.GetService<INodeSessionService>();
        var taskExecutionInstance = Properties["TaskExecutionInstance"] as TaskExecutionInstanceModel;
        await _batchQueue.SendAsync(new TaskCancellationParameters(taskExecutionInstance.Id, nameof(TaskExecutionTimeLimitJob), Dns.GetHostName()));
    }
}