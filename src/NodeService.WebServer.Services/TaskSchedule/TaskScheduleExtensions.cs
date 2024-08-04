using Microsoft.Extensions.DependencyInjection;
using Quartz.Impl;
using Quartz.Spi;

namespace NodeService.WebServer.Services.TaskSchedule
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
            services.AddKeyedSingleton<TaskSchedulerDictionary>(nameof(TaskScheduleService));
            services.AddSingleton<IAsyncQueue<KafkaDelayMessage>, AsyncQueue<KafkaDelayMessage>>();
            services.AddSingleton<IDelayMessageBroadcast, KafkaDelayMesageBroadcast>();
            services.AddHostedService<KafkaDelayMessageQueueService>();
            services.AddSingleton<JobScheduler>();

            services.AddSingleton<ISchedulerFactory>(new StdSchedulerFactory());


            return services;
        }

    }
}
