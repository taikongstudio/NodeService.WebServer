using System.Security.Claims;
using System.Security.Cryptography;
using Microsoft.AspNetCore.Components.Server.ProtectedBrowserStorage;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Configuration;
using NodeService.Infrastructure.Identity;
using NodeService.Infrastructure.Interfaces;

namespace NodeService.WebServer.Services.Auth;

public class LoginService
{
    const string AccessToken = nameof(AccessToken);
    const string RefreshToken = nameof(RefreshToken);
    readonly IBackendApiHttpClient _backendApiHttpClient;
    readonly IConfiguration _configuration;

    readonly ProtectedLocalStorage _localStorage;
    readonly NavigationManager _navigation;
    readonly SignInManager<IdentityUser> _signInManager;

    public LoginService(ProtectedLocalStorage localStorage,
        NavigationManager navigation,
        IConfiguration configuration,
        IBackendApiHttpClient backendApiHttpClient
    )
    {
        _localStorage = localStorage;
        _navigation = navigation;
        _configuration = configuration;
        _backendApiHttpClient = backendApiHttpClient;
    }

    public async Task<bool> LoginAsync(LoginModel model)
    {
        var response = await _backendApiHttpClient.LoginUserAsync(model);
        if (string.IsNullOrEmpty(response?.Result?.JwtToken))
            return false;

        await _localStorage.SetAsync(AccessToken, response.Result.JwtToken);
        await _localStorage.SetAsync(RefreshToken, response.Result.RefreshToken);

        return true;
    }


    public async Task<List<Claim>> GetLoginInfoAsync()
    {
        var emptyResult = new List<Claim>();
        ProtectedBrowserStorageResult<string> accessToken;
        ProtectedBrowserStorageResult<string> refreshToken;
        try
        {
            accessToken = await _localStorage.GetAsync<string>(AccessToken);
            refreshToken = await _localStorage.GetAsync<string>(RefreshToken);
        }
        catch (CryptographicException)
        {
            await LogoutAsync();
            return emptyResult;
        }

        if (accessToken.Success is false || accessToken.Value == default)
            return emptyResult;

        var claims = JwtTokenHelper.ValidateDecodeToken(accessToken.Value, _configuration);

        if (claims.Count != 0)
            return claims;

        if (refreshToken.Value != default)
        {
            var response = await _backendApiHttpClient.RefreshTokenAsync(refreshToken.Value);
            if (string.IsNullOrWhiteSpace(response?.Result?.JwtToken) is false)
            {
                await _localStorage.SetAsync(AccessToken, response.Result.JwtToken);
                await _localStorage.SetAsync(RefreshToken, response.Result.RefreshToken);
                claims = JwtTokenHelper.ValidateDecodeToken(response.Result.JwtToken, _configuration);
                return claims;
            }

            await LogoutAsync();
        }
        else
        {
            await LogoutAsync();
        }

        return claims;
    }

    public async Task LogoutAsync()
    {
        await RemoveAuthDataFromStorageAsync();
        _navigation.NavigateTo("/", true);
    }

    async Task RemoveAuthDataFromStorageAsync()
    {
        await _localStorage.DeleteAsync(AccessToken);
        await _localStorage.DeleteAsync(RefreshToken);
    }
}