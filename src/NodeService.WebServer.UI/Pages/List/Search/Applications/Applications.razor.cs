using AntDesign;
using Microsoft.AspNetCore.Components;
using NodeService.WebServer.UI.Models;
using NodeService.WebServer.UI.Services;

namespace NodeService.WebServer.UI.Pages.List;

public partial class Applications
{
    readonly ListGridType _listGridType = new()
    {
        Gutter = 16,
        Xs = 1,
        Sm = 2,
        Md = 3,
        Lg = 3,
        Xl = 4,
        Xxl = 4
    };

    readonly ListFormModel _model = new();
    readonly IList<string> _selectCategories = new List<string>();

    IList<ListItemDataType> _fakeList = new List<ListItemDataType>();


    [Inject] public IProjectService ProjectService { get; set; }

    protected override async Task OnInitializedAsync()
    {
        await base.OnInitializedAsync();
        _fakeList = await ProjectService.GetFakeListAsync(8);
    }
}