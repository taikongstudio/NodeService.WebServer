using Microsoft.Extensions.DependencyInjection;
using NodeService.WebServer.Services.NodeSessions;
using NodeService.WebServer.Services.TaskSchedule;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace NodeService.WebServer.Services.Tasks
{
    public static class TaskExtensions
    {
        public static IServiceCollection AddTaskServices(this IServiceCollection services)
        {
            services.AddSingleton<IAsyncQueue<TaskExecutionEventRequest>, AsyncQueue<TaskExecutionEventRequest>>();
            services.AddSingleton<IAsyncQueue<NodeHealthyCheckFireEvent>, AsyncQueue<NodeHealthyCheckFireEvent>>();
            services.AddSingleton<IAsyncQueue<AsyncOperation<TaskScheduleServiceParameters, TaskScheduleServiceResult>>, AsyncQueue<AsyncOperation<TaskScheduleServiceParameters, TaskScheduleServiceResult>>>();
            services.AddSingleton(new BatchQueue<TaskActivateServiceParameters>(TimeSpan.FromSeconds(1), 64));
            services.AddSingleton(new BatchQueue<TaskCancellationParameters>(TimeSpan.FromSeconds(1), 64));
            services.AddKeyedSingleton(nameof(TaskLogKafkaProducerService), new BatchQueue<TaskLogUnit>(TimeSpan.FromSeconds(1), 2048));
            services.AddKeyedSingleton(nameof(TaskLogPersistenceService), new BatchQueue<AsyncOperation<TaskLogUnit[]>>(TimeSpan.FromSeconds(5), 2048));
            services.AddSingleton<ITaskPenddingContextManager, TaskPenddingContextManager>();
            services.AddSingleton(new BatchQueue<AsyncOperation<TaskLogQueryServiceParameters, TaskLogQueryServiceResult>>(TimeSpan.FromSeconds(15), 2048));
        services.AddSingleton<TaskFlowExecutor>();
            services.AddSingleton<TaskActivationRecordExecutor>();
            services.AddSingleton<IAsyncQueue<TaskExecutionReport>, AsyncQueue<TaskExecutionReport>>();
            services.AddHostedService<TaskExecutionReportKafkaProducerService>();
            return services;
        }

    }
}
