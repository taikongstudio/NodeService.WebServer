using Microsoft.AspNetCore.Components;

namespace NodeService.WebServer.UI.Pages.Dashboard.Monitor;

public partial class WaterWave
{
    [Parameter] public string Title { get; set; }

    [Parameter] public int Percent { get; set; }

    [Parameter] public int? Height { get; set; }
}