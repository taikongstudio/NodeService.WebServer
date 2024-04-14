namespace NodeService.WebServer.Controllers
{
    public partial class ClientUpdateController
    {
        [HttpPost("/api/clientupdate/counters/addorupdate")]
        public async Task<ApiResponse<bool>>
            AddOrUpdateCountersAsync([FromBody] AddOrUpdateCounterParameters model)
        {
            ApiResponse<bool> apiResponse = new ApiResponse<bool>();
            try
            {
                apiResponse.Result = true;
                return apiResponse;
                using var dbContext = this._dbContextFactory.CreateDbContext();
                var remoteIpAddress = this.HttpContext.Connection.RemoteIpAddress.MapToIPv4().ToString();
                var updateConfig = await dbContext.ClientUpdateConfigurationDbSet.FindAsync(model.ClientUpdateConfigId);
                var counter = updateConfig.Counters.FirstOrDefault(x => x.IpAddress == remoteIpAddress && x.CategoryName == model.CategoryName);
                if (counter == null)
                {
                    counter = new ClientUpdateCounterModel()
                    {
                        IpAddress = remoteIpAddress,
                        CategoryName = model.CategoryName,
                        DnsName = model.NodeName,
                        CountValue = 1,
                    };
                    var newCounterList = updateConfig.Counters.ToList();
                    newCounterList.Add(counter);
                    updateConfig.Counters = newCounterList;
                }
                else
                {
                    var newCounterList = updateConfig.Counters.ToList();
                    var newCounter = new ClientUpdateCounterModel()
                    {
                        CategoryName = model.CategoryName,
                        CountValue = counter.CountValue + 1,
                        IpAddress = remoteIpAddress,
                        DnsName = model.NodeName,
                    };
                    newCounterList.Remove(counter);
                    newCounterList.Add(newCounter);
                    updateConfig.Counters = newCounterList;
                }
                await dbContext.SaveChangesAsync();
                apiResponse.Result = true;
            }
            catch (Exception ex)
            {
                apiResponse.ErrorCode = ex.HResult;
                apiResponse.Message = ex.Message;
            }
            return apiResponse;
        }

    }
}
