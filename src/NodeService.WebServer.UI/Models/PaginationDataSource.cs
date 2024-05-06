using AntDesign;
using NodeService.Infrastructure.Models;

namespace NodeService.WebServer.UI.Models;

public class PaginationDataSource<TElement, TQueryParameters>
    where TElement : class, new()
    where TQueryParameters : PaginationQueryParameters, new()
{
    private readonly Func<QueryParameters, CancellationToken, Task<PaginationResponse<TElement>>> _queryFunc;
    private readonly Action _stateChangedAction;

    public PaginationDataSource(
        Func<QueryParameters, CancellationToken, Task<PaginationResponse<TElement>>> queryHandler,
        Action stateChangedAction)
    {
        _queryFunc = queryHandler;
        _stateChangedAction = stateChangedAction;
    }

    public int PageIndex { get; set; } = 1;

    public int TotalCount { get; set; }

    public int PageSize { get; set; } = 10;

    public bool IsLoading { get; set; }

    public Func<TElement, Task> ItemInitializer { get; set; }

    public IEnumerable<TElement> ItemsSource { get; private set; } = [];

    public TQueryParameters QueryParameters { get; } = new();

    public virtual async Task RefreshAsync()
    {
        IsLoading = true;
        RaiseStateChanged();
        QueryParameters.QueryStrategy = QueryStrategy.Query;
        var rsp = await _queryFunc.Invoke(QueryParameters, default);
        PageSize = rsp.PageSize;
        PageIndex = rsp.PageIndex;
        TotalCount = Math.Max(rsp.TotalCount, rsp.PageSize);
        await InitItemsAsync(rsp.Result);
        IsLoading = false;
        RaiseStateChanged();
    }

    protected void RaiseStateChanged()
    {
        _stateChangedAction();
    }

    private async Task InitItemsAsync(IEnumerable<TElement> itemsSource)
    {
        if (itemsSource != null && ItemInitializer != null)
            foreach (var item in itemsSource)
                await ItemInitializer.Invoke(item);
        ItemsSource = itemsSource ?? [];
    }


    public virtual async Task OnPaginationEvent(PaginationEventArgs e)
    {
        QueryParameters.PageIndex = e.Page;
        QueryParameters.PageSize = e.PageSize;
        await RefreshAsync();
    }
}