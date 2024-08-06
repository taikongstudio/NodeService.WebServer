using Microsoft.Extensions.Hosting;
using NodeService.WebServer.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace NodeService.WebServer.Services.Tasks
{
    public class TaskObservationKafkaProduceService : BackgroundService
    {
        public TaskObservationKafkaProduceService(
            IAsyncQueue<TaskObservationEvent> eventQueue)
        {

        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            await Task.CompletedTask;
        }
    }
}
