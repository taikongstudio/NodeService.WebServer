using AntDesign;
using NodeService.Infrastructure.Models;

namespace NodeService.WebServer.UI.Models;

public class PaginationDataSource<TElement, TQueryParameters>
    where TElement : class, new()
    where TQueryParameters : PaginationQueryParameters, new()
{
    private readonly Func<QueryParameters, CancellationToken, Task<PaginationResponse<TElement?>>> _queryFunc;
    private readonly Action _stateChangedAction;

    public PaginationDataSource(
        Func<QueryParameters, CancellationToken, Task<PaginationResponse<TElement?>>> queryHandler,
        Action stateChangedAction)
    {
        _queryFunc = queryHandler;
        _stateChangedAction = stateChangedAction;
    }

    public int PageIndex { get; set; } = 1;

    public int TotalCount { get; set; }

    public int PageSize { get; set; } = 10;

    public bool IsLoading { get; set; }

    public Func<TElement?, CancellationToken, ValueTask> ItemInitializer { get; set; }

    public Func<Exception, Task> ExceptionHandler { get; set; }

    public Func<Task> Ready { get; set; }

    public Func<Task> Completed { get; set; }

    public IEnumerable<TElement?> ItemsSource { get; set; } = [];

    public TQueryParameters QueryParameters { get; } = new();

    public DateTime LastQueryDateTime { get; set; }

    public TimeSpan MinRefreshDuration { get; set; } = TimeSpan.FromSeconds(1);

    public virtual async Task QueryAsync()
    {
        if (IsLoading || DateTime.Now - LastQueryDateTime < MinRefreshDuration) return;
        try
        {
            LastQueryDateTime = DateTime.Now;
            IsLoading = true;
            RaiseStateChanged();
            QueryParameters.QueryStrategy = QueryStrategy.QueryPreferred;
            if (Ready != null) await Ready();
            var rsp = await _queryFunc.Invoke(QueryParameters, default);
            if (rsp.ErrorCode != 0)
                if (ExceptionHandler != null)
                {
                    await ExceptionHandler(new Exception(rsp.Message) { HResult = rsp.ErrorCode });
                    return;
                }

            if (rsp.TotalCount == 0 && TotalCount == 0)
            {
                await RaiseCompleted();
                return;
            }

            PageSize = rsp.PageSize;
            PageIndex = rsp.PageIndex;
            TotalCount = Math.Max(rsp.TotalCount, rsp.PageSize);
            await InvokeItemInitializerAsync(rsp.Result);
            ItemsSource = rsp.Result ?? [];
            await RaiseCompleted();
        }
        catch (Exception ex)
        {
            if (ExceptionHandler != null) await ExceptionHandler(ex);
        }
        finally
        {
            IsLoading = false;
            RaiseStateChanged();
        }
    }

    private async Task RaiseCompleted()
    {
        if (Completed != null) await Completed();
    }

    protected void RaiseStateChanged()
    {
        _stateChangedAction();
    }

    private async Task InvokeItemInitializerAsync(IEnumerable<TElement?>? itemsSource)
    {
        if (itemsSource != null && ItemInitializer != null)
            await Parallel.ForEachAsync(itemsSource, ItemInitializer);
    }


    public virtual async Task OnPaginationEvent(PaginationEventArgs e)
    {
        QueryParameters.PageIndex = e.Page;
        QueryParameters.PageSize = e.PageSize;
        await QueryAsync();
    }
}