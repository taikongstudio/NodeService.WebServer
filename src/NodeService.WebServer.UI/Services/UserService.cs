using System.Net.Http.Json;
using NodeService.WebServer.UI.Models;

namespace NodeService.WebServer.UI.Services;

public interface IUserService
{
    Task<CurrentUser> GetCurrentUserAsync();
}

public class UserService : IUserService
{
    readonly HttpClient _httpClient;

    public UserService(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public async Task<CurrentUser> GetCurrentUserAsync()
    {
        return await _httpClient.GetFromJsonAsync<CurrentUser>("data/current_user.json");
    }
}