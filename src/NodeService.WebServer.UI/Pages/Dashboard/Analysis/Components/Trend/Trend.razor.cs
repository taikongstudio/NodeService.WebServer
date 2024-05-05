using Microsoft.AspNetCore.Components;

namespace NodeService.WebServer.UI.Pages.Dashboard.Analysis;

public partial class Trend
{
    [Parameter] public RenderFragment ChildContent { get; set; }

    [Parameter] public string Flag { get; set; }
}