using AntDesign.Charts;
using Microsoft.AspNetCore.Components;
using NodeService.WebServer.UI.Services;

namespace NodeService.WebServer.UI.Pages.Dashboard.Analysis;

public partial class SalesCard
{
    readonly ColumnConfig _saleChartConfig = new()
    {
        AutoFit = true,
        Padding = "auto",
        XField = "x",
        YField = "y"
    };

    readonly ColumnConfig _visitChartConfig = new()
    {
        AutoFit = true,
        Padding = "auto",
        XField = "x",
        YField = "y"
    };

    IChartComponent _saleChart;
    IChartComponent _visitChart;

    [Parameter]
    public SaleItem[] Items { get; set; } =
    {
        new() { Id = 1, Title = "Gongzhuan No.0 shop", Total = "323,234" },
        new() { Id = 2, Title = "Gongzhuan No.1 shop", Total = "323,234" },
        new() { Id = 3, Title = "Gongzhuan No.2 shop", Total = "323,234" },
        new() { Id = 4, Title = "Gongzhuan No.3 shop", Total = "323,234" },
        new() { Id = 5, Title = "Gongzhuan No.4 shop", Total = "323,234" },
        new() { Id = 6, Title = "Gongzhuan No.5 shop", Total = "323,234" },
        new() { Id = 7, Title = "Gongzhuan No.6 shop", Total = "323,234" }
    };

    [Inject] public IChartService ChartService { get; set; }

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        await base.OnAfterRenderAsync(firstRender);
        if (firstRender) await OnTabChanged("1");
    }

    async Task OnTabChanged(string activeKey)
    {
        var data = await ChartService.GetSalesDataAsync();
        if (activeKey == "1")
            await _saleChart.ChangeData(data);
        else
            await _visitChart.ChangeData(data);
    }
}