using System.Web;
using AntDesign;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Routing;
using NodeService.Infrastructure.Models;

namespace NodeService.WebServer.UI.Models;

public class RoutedPaginationDataSource<TElement, TQueryParameters> : PaginationDataSource<TElement, TQueryParameters>,
    IDisposable
    where TElement : class, new()
    where TQueryParameters : PaginationQueryParameters, new()
{
    private readonly string _baseAddress;
    private string _currentQuery;
    private string _uri;
    private long _disposed;

    public RoutedPaginationDataSource(
        string baseAddress,
        Func<QueryParameters, CancellationToken, Task<PaginationResponse<TElement?>>> queryFunc,
        Action stateChangedAction) : base(queryFunc, stateChangedAction)
    {
        _baseAddress = baseAddress;
    }

    public void Dispose()
    {

    }

    public override async Task QueryAsync()
    {
        var uri = GetUri();
        var query = HttpUtility.ParseQueryString(uri.Query);

        if (int.TryParse(query.Get("pageindex"), out var pageIndex) &&
            int.TryParse(query.Get("pagesize"), out var pageSize))
        {
            PageIndex = pageIndex;
            PageSize = pageSize;
        }

        await base.QueryAsync();
    }

    private Uri GetUri()
    {
        //if (_uri == null) return new Uri(_navigationManager.Uri);
        //return _navigationManager.ToAbsoluteUri(_uri);
        return new Uri(_uri);
    }

    public override async Task OnPaginationEvent(PaginationEventArgs e)
    {
        var pageIndex = e.Page;
        var pageSize = e.PageSize;
        await LoadPageAsunc(pageIndex, pageSize);
    }

    public async Task LoadPageAsunc(int pageIndex, int pageSize)
    {
        PageIndex = pageIndex;
        PageSize = pageSize;
        await RequestAsync();
    }

    public async Task RequestAsync()
    {
       await RequestCoreAsync(false);
    }

    public async Task ForceRequestAsync()
    {
       await RequestCoreAsync(true);
    }

    private async Task RequestCoreAsync(bool force)
    {
        if (PageIndex <= 0) PageIndex = 1;
        if (PageSize <= 0) PageSize = 10;
        QueryParameters.PageIndex = PageIndex;
        QueryParameters.PageSize = PageSize;
        QueryParameters.QueryStrategy = QueryStrategy.QueryPreferred;
        if (_uri == null)
        {
            _uri = _baseAddress;
        }
        var uri = new Uri(_uri);
        var uriBuilder = new UriBuilder(uri);
        var oldQuery = uriBuilder.Query;
        uriBuilder.Query = string.Empty;
        uri = uriBuilder.Uri;
        _currentQuery = $"?{QueryParameters}";
        if (force || (oldQuery != _currentQuery && !IsLoading))
        {
            _uri = $"{uri}{_currentQuery}";
            await QueryAsync();
        }
    }
}