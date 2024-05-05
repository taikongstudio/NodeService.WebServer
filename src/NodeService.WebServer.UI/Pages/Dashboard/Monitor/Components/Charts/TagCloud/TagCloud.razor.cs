using Microsoft.AspNetCore.Components;

namespace NodeService.WebServer.UI.Pages.Dashboard.Monitor;

public partial class TagCloud
{
    [Parameter] public object[] Data { get; set; }

    [Parameter] public int? Height { get; set; }
}