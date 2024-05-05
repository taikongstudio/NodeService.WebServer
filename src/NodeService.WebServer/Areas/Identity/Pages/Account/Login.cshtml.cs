using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace NodeService.WebServer.Areas.Identity.Pages.Account;

[AllowAnonymous]
public class LoginPage : PageModel
{
    public void OnGet()
    {
    }
}