using NodeService.WebServer.Data.Repositories.Specifications;

namespace NodeService.WebServer.Controllers;

public partial class ClientUpdateController
{
    [HttpPost("/api/clientupdate/counters/AddOrUpdate")]
    public async Task<ApiResponse<bool>>
        AddOrUpdateCountersAsync([FromBody] AddOrUpdateCounterParameters model)
    {
        var apiResponse = new ApiResponse<bool>();
        try
        {
            using var repo = _clientUpdateCounterRepoFactory.CreateRepository();
            var counter = await repo.FirstOrDefaultAsync(new ClientUpdateCounterSpecification(
                model.ClientUpdateConfigId,
                model.NodeName));
            if (counter == null)
            {
                counter = new ClientUpdateCounterModel
                {
                    Id = model.ClientUpdateConfigId,
                    Name = model.NodeName,
                    Counters = [new ClientUpdateCategoryModel { CategoryName = model.CategoryName, CountValue = 1 }]
                };
                await repo.AddAsync(counter);
            }
            else
            {

                var newCounterList = counter.Counters.ToList();
                ClientUpdateCategoryModel? category = null;
                foreach (var item in newCounterList)
                    if (item.CategoryName == model.CategoryName)
                    {
                        category = item;
                        break;
                    }

                if (category == null)
                {
                    category = new ClientUpdateCategoryModel { CategoryName = model.CategoryName, CountValue = 1 };
                    newCounterList.Add(category);
                }
                else
                {
                    category.CountValue++;
                }

                counter.Counters = newCounterList;
                await repo.SaveChangesAsync();
            }



            apiResponse.SetResult(true);
        }
        catch (Exception ex)
        {
            _exceptionCounter.AddOrUpdate(ex);
            _logger.LogError(ex.ToString());
            apiResponse.ErrorCode = ex.HResult;
            apiResponse.Message = ex.Message;
        }

        return apiResponse;
    }
}