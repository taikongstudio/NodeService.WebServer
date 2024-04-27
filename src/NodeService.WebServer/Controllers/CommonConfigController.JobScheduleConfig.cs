namespace NodeService.WebServer.Controllers
{
    public partial class CommonConfigController
    {


        [HttpPost("/api/commonconfig/jobschedule/addorupdate")]
        public Task<ApiResponse> AddOrUpdateAsync([FromBody] JobScheduleConfigModel model)
        {
            return AddOrUpdateConfigurationAsync(model, AddOrUpdateJobScheduleConfigAsync);
        }

        private async Task AddOrUpdateJobScheduleConfigAsync(JobScheduleConfigModel jobScheduleConfig)
        {
            await _serviceProvider.GetService<IAsyncQueue<TaskScheduleMessage>>().EnqueueAsync(
                    new(TaskTriggerSource.Schedule, jobScheduleConfig.Id, jobScheduleConfig.TriggerType == JobScheduleTriggerType.Manual));
        }




        [HttpPost("/api/commonconfig/jobschedule/{jobScheduleId}/invoke")]
        public async Task<ApiResponse> InvokeJobScheduleAsync(string jobScheduleId, [FromBody] InvokeJobScheduleParameters invokeJobScheduleParameters)
        {
            ApiResponse apiResponse = new ApiResponse();
            try
            {
                await _serviceProvider.GetService<IAsyncQueue<TaskScheduleMessage>>().EnqueueAsync(
                    new(TaskTriggerSource.Manual, jobScheduleId));
            }
            catch (Exception ex)
            {
                apiResponse.ErrorCode = ex.HResult;
                apiResponse.Message = ex.Message;
            }
            return apiResponse;
        }



        [HttpGet("/api/commonconfig/jobschedule/list")]
        public Task<PaginationResponse<JobScheduleConfigModel>> QueryJobScheduleConfigurationListAsync([FromQuery] QueryParametersModel queryParameters)
        {
            return this.QueryConfigurationListAsync<JobScheduleConfigModel>(queryParameters);
        }

        [HttpGet("/api/commonconfig/jobschedule/{id}")]
        public Task<ApiResponse<JobScheduleConfigModel>> QueryJobScheduleConfigAsync(string id)
        {
            return this.QueryConfigurationAsync<JobScheduleConfigModel>(id);
        }


        [HttpPost("/api/commonconfig/jobschedule/remove")]
        public Task<ApiResponse> RemoveAsync([FromBody] JobScheduleConfigModel model)
        {
            return RemoveConfigurationAsync(model, RemoveJobScheduleConfigAsync);
        }

        private async Task RemoveJobScheduleConfigAsync(JobScheduleConfigModel jobScheduleConfig)
        {
            var messageQueue = this._serviceProvider.GetService<IAsyncQueue<TaskScheduleMessage>>();
            await messageQueue.EnqueueAsync(new(TaskTriggerSource.Schedule, jobScheduleConfig.Id, true));
        }


    }
}
