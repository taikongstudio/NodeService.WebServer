﻿using Microsoft.Extensions.DependencyInjection;
using NodeService.WebServer.Services.NodeSessions;
using Quartz.Impl;
using Quartz.Spi;

namespace NodeService.WebServer.Services.Tasks
{
    public class TaskScheduleSetupOptions
    {
        public string RedisConnectionString { get; set; }

        public string TaskLogDbName { get; set; }
    }

    public static class TaskScheduleExtensions
    {
        public static IServiceCollection AddTaskSchedule(this IServiceCollection services, Action<TaskScheduleSetupOptions> setupAction)
        {
            ArgumentNullException.ThrowIfNull(services, nameof(services));
            ArgumentNullException.ThrowIfNull(setupAction, nameof(setupAction));
            services.AddOptions();
            services.Configure(setupAction);
            services.AddSingleton<IJobFactory, JobFactory>();
            services.AddSingleton<TaskSchedulerDictionary>();
            services.AddSingleton<JobScheduler>();
            services.AddSingleton<TaskFlowExecutor>();
            services.AddSingleton<ISchedulerFactory>(new StdSchedulerFactory());
            services.AddSingleton<IAsyncQueue<TaskExecutionEventRequest>, AsyncQueue<TaskExecutionEventRequest>>();
            services.AddSingleton<IAsyncQueue<AsyncOperation<TaskScheduleServiceParameters, TaskScheduleServiceResult>>, AsyncQueue<AsyncOperation<TaskScheduleServiceParameters, TaskScheduleServiceResult>>>();
            services.AddSingleton(new BatchQueue<TaskActivateServiceParameters>(TimeSpan.FromSeconds(1), 64));
            services.AddSingleton(new BatchQueue<TaskCancellationParameters>(TimeSpan.FromSeconds(1), 64));
            services.AddKeyedSingleton(nameof(TaskLogKafkaProducerService), new BatchQueue<TaskLogUnit>(TimeSpan.FromSeconds(1), 2048));
            services.AddKeyedSingleton(nameof(TaskLogPersistenceService), new BatchQueue<AsyncOperation<TaskLogUnit[]>>(TimeSpan.FromSeconds(5), 2048));
            services.AddSingleton<ITaskPenddingContextManager, TaskPenddingContextManager>();
            services.AddSingleton(new BatchQueue<AsyncOperation<TaskLogQueryServiceParameters, TaskLogQueryServiceResult>>(TimeSpan.FromSeconds(15), 2048));
            services.AddSingleton(new BatchQueue<TaskExecutionReportMessage>(TimeSpan.FromSeconds(3), 1024));

            return services;
        }

    }
}
