using AntDesign;
using Microsoft.AspNetCore.Components;
using NodeService.WebServer.UI.Models;
using NodeService.WebServer.UI.Services;

namespace NodeService.WebServer.UI.Pages.List;

public partial class CardList
{
    private readonly ListGridType _listGridType = new()
    {
        Gutter = 16,
        Xs = 1,
        Sm = 2,
        Md = 3,
        Lg = 3,
        Xl = 4,
        Xxl = 4
    };

    private ListItemDataType[] _data = { };

    [Inject] protected IProjectService ProjectService { get; set; }

    protected override async Task OnInitializedAsync()
    {
        await base.OnInitializedAsync();
        var list = new List<ListItemDataType> { new() };
        var data = await ProjectService.GetFakeListAsync(8);
        list.AddRange(data);
        _data = list.ToArray();
    }
}