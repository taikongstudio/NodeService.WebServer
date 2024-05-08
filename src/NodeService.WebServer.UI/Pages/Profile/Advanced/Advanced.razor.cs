using AntDesign.ProLayout;
using Microsoft.AspNetCore.Components;
using NodeService.WebServer.UI.Models;
using NodeService.WebServer.UI.Services;

namespace NodeService.WebServer.UI.Pages.Profile;

public partial class Advanced
{
    readonly IList<TabPaneItem> _tabList = new List<TabPaneItem>
    {
        new() { Key = "detail", Tab = "Details" },
        new() { Key = "rules", Tab = "Rules" }
    };

    AdvancedProfileData _data = new();

    [Inject] protected IProfileService ProfileService { get; set; }

    protected override async Task OnInitializedAsync()
    {
        await base.OnInitializedAsync();
        _data = await ProfileService.GetAdvancedAsync();
    }
}