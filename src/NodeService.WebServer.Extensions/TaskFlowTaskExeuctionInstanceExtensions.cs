using NodeService.Infrastructure.DataModels;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace NodeService.WebServer.Extensions
{
    public static class TaskFlowTaskExeuctionInstanceExtensions
    {
        public static async Task CancelTaskAsync(
            this TaskFlowTaskExecutionInstance taskFlowTaskExeuctionInstance,
            CancellationToken cancellationToken = default)
        {
            await Task.CompletedTask;
        }

        public static async Task RerunTaskAsync(
    this TaskFlowTaskExecutionInstance taskFlowTaskExeuctionInstance,
    CancellationToken cancellationToken = default)
        {
            await Task.CompletedTask;
        }

        public static async Task RetryTaskAsync(
this TaskFlowTaskExecutionInstance taskFlowTaskExeuctionInstance,
CancellationToken cancellationToken = default)
        {
            await Task.CompletedTask;
        }

    }
}
