using Microsoft.AspNetCore.Components;
using NodeService.WebServer.UI.Models;
using NodeService.WebServer.UI.Services;

namespace NodeService.WebServer.UI.Pages.Profile;

public partial class Basic
{
    private BasicProfileDataType _data = new();

    [Inject] protected IProfileService ProfileService { get; set; }

    protected override async Task OnInitializedAsync()
    {
        await base.OnInitializedAsync();
        _data = await ProfileService.GetBasicAsync();
    }
}