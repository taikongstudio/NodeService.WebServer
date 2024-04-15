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
                using var dbContext = this._dbContextFactory.CreateDbContext();
                var counter = await dbContext.ClientUpdateCountersDbSet.FindAsync(model.ClientUpdateConfigId, model.NodeName);
                if (counter == null)
                {
                    counter = new ClientUpdateCounterModel()
                    {
                        Id = model.ClientUpdateConfigId,
                        Name = model.NodeName,
                    };
                    await dbContext.ClientUpdateCountersDbSet.AddAsync(counter);
                }
                var newCounterList = counter.Counters.ToList();
                CategoryModel? category = null;
                foreach (var item in newCounterList)
                {
                    if (item.CategoryName == model.CategoryName)
                    {
                        category = item;
                        break;
                    }
                }
                if (category == null)
                {
                    category = new CategoryModel() { CategoryName = model.CategoryName, CountValue = 1 };
                    newCounterList.Add(category);
                }
                else
                {
                    category.CountValue++;
                }

                counter.Counters = newCounterList;
                
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
