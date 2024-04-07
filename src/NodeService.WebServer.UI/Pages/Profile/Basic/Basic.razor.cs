using Microsoft.AspNetCore.Components;
using NodeService.WebServer.UI.Models;
using NodeService.WebServer.UI.Services;
using System.Threading.Tasks;

namespace NodeService.WebServer.UI.Pages.Profile
{
    public partial class Basic
    {
        private BasicProfileDataType _data = new BasicProfileDataType();

        [Inject] protected IProfileService ProfileService { get; set; }

        protected override async Task OnInitializedAsync()
        {
            await base.OnInitializedAsync();
            _data = await ProfileService.GetBasicAsync();
        }
    }
}