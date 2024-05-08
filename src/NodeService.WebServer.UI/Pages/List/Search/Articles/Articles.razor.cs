using Microsoft.AspNetCore.Components;
using NodeService.WebServer.UI.Models;
using NodeService.WebServer.UI.Services;

namespace NodeService.WebServer.UI.Pages.List;

public partial class Articles
{
    readonly string[] _defaultOwners = { "wzj", "wjh" };
    readonly ListFormModel _model = new();

    readonly Owner[] _owners =
    {
        new() { Id = "wzj", Name = "Myself" },
        new() { Id = "wjh", Name = "Wu Jiahao" },
        new() { Id = "zxx", Name = "Zhou Xingxing" },
        new() { Id = "zly", Name = "Zhao Liying" },
        new() { Id = "ym", Name = "Yao Ming" }
    };

    IList<ListItemDataType> _fakeList = new List<ListItemDataType>();

    [Inject] public IProjectService ProjectService { get; set; }

    void SetOwner()
    {
    }

    protected override async Task OnInitializedAsync()
    {
        await base.OnInitializedAsync();
        _fakeList = await ProjectService.GetFakeListAsync(8);
    }
}