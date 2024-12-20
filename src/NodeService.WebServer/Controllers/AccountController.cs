using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using NodeService.Infrastructure.Identity;
using NodeService.Infrastructure.Permissions;

namespace NodeService.WebServer.Controllers;

[ApiController]
[Route("api/[controller]/[action]")]
public class AccountController : ControllerBase
{
    private readonly IAccessControlService _accessControl;
    private readonly ExceptionCounter _exceptionCounter;

    private readonly ILogger<AccountController> _logger;
    private readonly IMemoryCache _memoryCache;
    private readonly int _refreshTokenExpiration;
    private readonly UserManager<ApplicationUser> _userManager;

    public AccountController(IAccessControlService accessControl,
        ExceptionCounter exceptionCounter,
        IOptions<JwtSettings> jwtSettings,
        IMemoryCache memoryChache,
        ILogger<AccountController> logger,
        UserManager<ApplicationUser> userManager)
    {
        _accessControl = accessControl;
        _memoryCache = memoryChache;
        _refreshTokenExpiration = jwtSettings.Value.RefreshExpirationTime;
        _logger = logger;
        _userManager = userManager;
        _exceptionCounter = exceptionCounter;
    }

    [HttpPost("/api/account/refresh")]
    [AllowAnonymous]
    public async Task<ActionResult<BaseApiResponse<AuthenticationResponse>>> RefreshToken(
        [FromBody] RefreshTokenInput input)
    {
        var refreshToken = input.RefreshToken;
        var userEmail = await _accessControl.RefreshTokenExists(refreshToken);

        if (string.IsNullOrWhiteSpace(userEmail)) return Unauthorized();

        if (_memoryCache.TryGetValue(userEmail, out _))
        {
            await _accessControl.SetUserRefreshToken(userEmail, "", TimeSpan.Zero);
            _memoryCache.Remove(userEmail);
            return Unauthorized();
        }

        var claims = await _accessControl.GetUserClaimsBy(userEmail);
        var jwtToken = _accessControl.GenerateJWTToken(claims);

        var newRefreshToken = _accessControl.GenerateRefreshToken();
        var updateSuccessfully = await _accessControl.SetUserRefreshToken(userEmail, newRefreshToken,
            TimeSpan.FromMinutes(_refreshTokenExpiration));
        if (!updateSuccessfully)
            return Conflict(new BaseApiResponse<AuthenticationResponse>
                { Errors = ["Cannot update refresh token"] });

        return Ok(new BaseApiResponse<AuthenticationResponse>
        {
            Result = new AuthenticationResponse
            {
                JwtToken = jwtToken,
                RefreshToken = newRefreshToken
            }
        });
    }

    [HttpPost("/api/account/login")]
    public async Task<ActionResult<BaseApiResponse<AuthenticationResponse>>> LoginAsync(
        [FromBody] LoginCredential credential)
    {
        var response = new BaseApiResponse<AuthenticationResponse>();
        if (!ModelState.IsValid)
        {
            response.AddModelErrors(ModelState);
            return BadRequest(response);
        }

        _logger.LogTrace($"Login Request recived for Email: {credential.Email}");
        var user = await _accessControl.GetUserClaimsBy(credential.Email);
        if (user == null)
        {
            _logger.LogError($"there is no user with email: {credential.Email} in identity database");
            return NotFound();
        }

        _logger.LogTrace($"Email: {credential.Email} has access to login");
        var passwordHasher = new PasswordHasher<ApplicationUser>();
        var appUser = await _userManager.FindByEmailAsync(credential.Email);
        var result = string.IsNullOrWhiteSpace(appUser?.PasswordHash) is false &&
                     passwordHasher.VerifyHashedPassword(appUser, appUser.PasswordHash, credential.Password) ==
                     PasswordVerificationResult.Success;
        if (!result)
        {
            _logger.LogError($"user with email: {credential.Email} has not access to LDAP");
            return Unauthorized();
        }

        var token = _accessControl.GenerateJWTToken(user);
        var refreshToken = _accessControl.GenerateRefreshToken();
        var updateSuccessfully = await _accessControl.SetUserRefreshToken(credential.Email, refreshToken,
            TimeSpan.FromMinutes(_refreshTokenExpiration));
        if (!updateSuccessfully)
            return Conflict(new BaseApiResponse<AuthenticationResponse>
                { Errors = ["Cannot update refresh token"] });

        return Ok(new BaseApiResponse<AuthenticationResponse>
        {
            Result = new AuthenticationResponse
            {
                JwtToken = token,
                RefreshToken = refreshToken
            }
        });
    }

    [HttpPost("/api/account/register")]
    [Authorize(Policy = PolicyTypes.Users.Manage)]
    public async Task<ActionResult<BaseApiResponse<string>>> Register([FromBody] UserRegisterInput input)
    {
        if (!ModelState.IsValid)
        {
            var response = new BaseApiResponse<string>();
            response.AddModelErrors(ModelState);
            return BadRequest(response);
        }

        var result = await _accessControl.CreateUser(input);
        if (!result.Any())
            return Ok(new BaseApiResponse<string>("OK"));

        return BadRequest(new BaseApiResponse<string> { Errors = result });
    }

    [HttpDelete("/api/account/user/{id}")]
    [Authorize(Policy = PolicyTypes.Users.Manage)]
    public async Task<ActionResult<BaseApiResponse<string>>> DisableUser([FromRoute] string id)
    {
        var result = await _accessControl.DisableUser(id);

        if (!result.Succeeded && result.UpdatedUser == null)
            return NotFound(new BaseApiResponse<string> { Errors = new List<string> { "User not found" } });
        _memoryCache.Set(result.UpdatedUser.Email, 1);
        return Ok(new BaseApiResponse<string>("done"));
    }

    [HttpGet("/api/account/user/{id}")]
    [Authorize(Policy = PolicyTypes.Users.Manage)]
    public async Task<ActionResult<BaseApiResponse<UserVM>>> GetUser([FromRoute] string id)
    {
        var result = await _accessControl.GetUsersById(id);
        return Ok(new BaseApiResponse<UserVM> { Result = result });
    }

    [HttpGet("/api/account/permission")]
    [Authorize(Policy = PolicyTypes.Users.Manage)]
    public ActionResult<BaseApiResponse<PermissionList>> GetAllPermissions()
    {
        return Ok(new BaseApiResponse<PermissionList>(new PermissionList()));
    }

    [HttpPost("/api/account/role")]
    [Authorize(Policy = PolicyTypes.Users.Manage)]
    public async Task<ActionResult<BaseApiResponse<List<string>>>> CreateRole([FromBody] RoleInput input)
    {
        var result = await _accessControl.CreateRole(input);
        return Ok(new BaseApiResponse<List<string>>(result));
    }

    [HttpGet("/api/account/role")]
    [Authorize(Policy = PolicyTypes.Users.Manage)]
    public async Task<ActionResult<BaseApiResponse<List<RoleItem>>>> GetAllRoles()
    {
        var result = await _accessControl.GetAllRoles();
        return Ok(new BaseApiResponse<List<RoleItem>>(result));
    }

    [HttpPut("/api/account/role/{id}")]
    [Authorize(Policy = PolicyTypes.Users.Manage)]
    public async Task<ActionResult<BaseApiResponse<bool>>> Update([FromRoute] string id, [FromBody] RoleInput roleInput)
    {
        var result = await _accessControl.UpdateRolePermissions(id, roleInput);
        var usersWithThisRole = await _accessControl.GetUsersByRoleId(id);
        usersWithThisRole.ForEach(x => { _memoryCache.Set(x.Email, 1); });
        return Ok(new BaseApiResponse<bool>(result));
    }

    [HttpGet("/api/account/user")]
    [Authorize(Policy = PolicyTypes.Users.View)]
    public async Task<ActionResult<BaseApiResponse<UserListVM>>> GetAllUsers([FromQuery] UserFilterInput filterInput)
    {
        if (filterInput.RowCount == default) filterInput.RowCount = 15;
        if (filterInput.PageNumber == default) filterInput.PageNumber = 1;

        var result = await _accessControl.GetUsers(filterInput);

        if (!result.Users.Any()) return NotFound();

        return Ok(new BaseApiResponse<UserListVM> { Result = result });
    }

    [HttpPut("/api/account/user/{id}")]
    [Authorize(Policy = PolicyTypes.Users.Manage)]
    public async Task<ActionResult<BaseApiResponse<bool>>> UpdateUser([FromRoute] string id,
        [FromBody] UpdateUserInput updateInput)
    {
        var result = await _accessControl.UpdateUser(id, updateInput);
        if (!result) return NotFound();

        return Ok(new BaseApiResponse<bool> { Result = result });
    }
}