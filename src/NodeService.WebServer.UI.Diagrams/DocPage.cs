using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;
using System.Threading.Tasks;

namespace NodeService.WebServer.UI.Diagrams;

public class DocPage : ComponentBase
{
    [Inject]
    private IJSRuntime JsRuntime { get; set; }

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        await base.OnAfterRenderAsync(firstRender);

        await JsRuntime.InvokeVoidAsync("setup");
    }
}
