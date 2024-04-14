namespace NodeService.WebServer.Controllers
{
    public partial class CommonConfigController
    {
        [HttpGet("/api/commonconfig/nodesetting/")]
        public async Task<ApiResponse<NodeSettings>> QueryNodeSettingsAsync()
        {
            ApiResponse<NodeSettings> rsp = new ApiResponse<NodeSettings>();
            try
            {
                NodeSettings? result = null;
                using var dbContext = _dbContextFactory.CreateDbContext();
                var notificationSourceDictionary = await dbContext.PropertyBagDbSet.FindAsync("NodeSettings");
                if (notificationSourceDictionary == null)
                {
                    notificationSourceDictionary = new Dictionary<string, object>();
                    result = new NodeSettings();
                    notificationSourceDictionary.Add("Id", "NodeSettings");
                    notificationSourceDictionary.Add("Value", JsonSerializer.Serialize(result));
                    notificationSourceDictionary["CreatedDate"] = DateTime.UtcNow;
                    dbContext.PropertyBagDbSet.Add(notificationSourceDictionary);
                    await dbContext.SaveChangesAsync();
                }
                else
                {
                    result = JsonSerializer.Deserialize<NodeSettings>(notificationSourceDictionary["Value"] as string);
                }
                rsp.Result = result;
            }
            catch (Exception ex)
            {
                rsp.ErrorCode = ex.HResult;
                rsp.Message = ex.ToString();
            }
            return rsp;
        }

        [HttpPost("/api/commonconfig/nodesetting/update")]
        public async Task<ApiResponse> UpdateNodeSettingAsync([FromBody] NodeSettings model)
        {
            ApiResponse rsp = new ApiResponse();
            try
            {
                using var dbContext = _dbContextFactory.CreateDbContext();
                var dictionary = await dbContext.PropertyBagDbSet.FindAsync("NodeSettings");

                dictionary["Value"] = JsonSerializer.Serialize(model);
                await dbContext.SaveChangesAsync();
            }
            catch (Exception ex)
            {
                rsp.ErrorCode = ex.HResult;
                rsp.Message = ex.ToString();
            }
            return rsp;
        }

    }
}
