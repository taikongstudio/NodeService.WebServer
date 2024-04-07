using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore.Metadata.Internal;
using NodeService.Infrastructure.Entities;

namespace NodeService.WebServer.Areas.Identity.Pages.Account
{
    [AllowAnonymous]
    public class LoginPage : PageModel
    {
        public void OnGet()
        {
        }

    }
}
