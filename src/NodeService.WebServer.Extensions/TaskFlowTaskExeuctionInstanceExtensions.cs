using NodeService.Infrastructure;
using NodeService.Infrastructure.DataModels;
using NodeService.Infrastructure.Models;
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
            ApiService apiService,
            CancellationToken cancellationToken = default)
        {
            await Task.CompletedTask;
        }

        public static async Task RerunTaskAsync(
            this TaskFlowTaskExecutionInstance taskFlowTaskExeuctionInstance,
            ApiService apiService,
            CancellationToken cancellationToken = default)
        {
            await Task.CompletedTask;
        }

        public static async Task RetryTaskAsync(
            this TaskFlowTaskExecutionInstance taskFlowTaskExeuctionInstance,
            ApiService apiService,
            CancellationToken cancellationToken = default)
        {
            await Task.CompletedTask;
        }

        public static async Task<TaskActivationRecordModel> QueryTaskFlowTaskDetailsAsync(
            this TaskFlowTaskExecutionInstance taskFlowTaskExeuctionInstance,
            ApiService apiService,
            CancellationToken cancellationToken = default)
        {
            var queryParameters = new QueryTaskActivationRecordListParameters()
            {
                Status = TaskExecutionStatus.Unknown,
                FireInstanceIdList = [taskFlowTaskExeuctionInstance.TaskActiveRecordId]
            };
            var rsp = await apiService.QueryTaskActivationRecordListAsync(queryParameters, cancellationToken);
            if (rsp.ErrorCode == 0)
            {
                return rsp.Result.FirstOrDefault();
            }
            return null;
        }

    }
}
