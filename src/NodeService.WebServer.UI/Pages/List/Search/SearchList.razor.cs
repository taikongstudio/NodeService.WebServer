using AntDesign.ProLayout;
using Microsoft.AspNetCore.Components;

namespace NodeService.WebServer.UI.Pages.List;

public partial class SearchList
{
    readonly IList<TabPaneItem> _tabList = new List<TabPaneItem>
    {
        new() { Key = "articles", Tab = "Articles" },
        new() { Key = "projects", Tab = "Projects" },
        new() { Key = "applications", Tab = "Applications" }
    };

    [Inject] protected NavigationManager NavigationManager { get; set; }

    string GetTabKey()
    {
        var url = NavigationManager.Uri.TrimEnd('/');
        var key = url.Substring(url.LastIndexOf('/') + 1);
        return key;
    }

    void HandleTabChange(string key)
    {
        var url = NavigationManager.Uri.TrimEnd('/');
        url = url.Substring(0, url.LastIndexOf('/'));
        NavigationManager.NavigateTo($"{url}/{key}");
    }
}