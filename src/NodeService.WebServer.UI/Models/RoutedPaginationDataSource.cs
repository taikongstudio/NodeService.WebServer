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
    private readonly IDisposable _locaitionChangingToken;
    private readonly NavigationManager _navigationManager;
    private string _currentQuery;
    private string _uri;

    public RoutedPaginationDataSource(
        NavigationManager navigationManager,
        Func<QueryParameters, CancellationToken, Task<PaginationResponse<TElement>>> queryFunc,
        Action stateChangedAction) : base(queryFunc, stateChangedAction)
    {
        _navigationManager = navigationManager;
        _locaitionChangingToken = _navigationManager.RegisterLocationChangingHandler(OnLocationChanging);
        _navigationManager.LocationChanged += NavigationManager_LocationChanged;
    }

    public void Dispose()
    {
        _navigationManager.LocationChanged -= NavigationManager_LocationChanged;
        _locaitionChangingToken.Dispose();
    }

    private async void NavigationManager_LocationChanged(object? sender, LocationChangedEventArgs e)
    {
        await RefreshAsync();
    }

    private async ValueTask OnLocationChanging(LocationChangingContext context)
    {
        var uri = new Uri(context.TargetLocation);
        if (string.IsNullOrEmpty(uri.Query)) return;
        if (_currentQuery == uri.Query && IsLoading)
        {
            context.PreventNavigation();
            return;
        }

        _uri = context.TargetLocation;
        IsLoading = true;
        RaiseStateChanged();
        await ValueTask.CompletedTask;
    }

    public override async Task RefreshAsync()
    {
        var uri = GetUri();
        var query = HttpUtility.ParseQueryString(uri.Query);

        if (int.TryParse(query.Get("pageindex"), out var pageIndex) &&
            int.TryParse(query.Get("pagesize"), out var pageSize))
        {
            PageIndex = pageIndex;
            PageSize = pageSize;
        }

        await base.RefreshAsync();
    }

    private Uri GetUri()
    {
        if (_uri == null) return new Uri(_navigationManager.Uri);
        return _navigationManager.ToAbsoluteUri(_uri);
    }

    public override Task OnPaginationEvent(PaginationEventArgs e)
    {
        var pageIndex = e.Page;
        var pageSize = e.PageSize;
        NavigateToPage(pageIndex, pageSize);
        return Task.CompletedTask;
    }

    public void NavigateToPage(int pageIndex, int pageSize)
    {
        PageIndex = pageIndex;
        PageSize = pageSize;
        Request();
    }

    public void Request()
    {
        RequestCore(false);
    }

    public void ForceRequest()
    {
        RequestCore(true);
    }

    private void RequestCore(bool force)
    {
        if (PageIndex <= 0) PageIndex = 1;
        if (PageSize <= 0) PageSize = 10;
        QueryParameters.PageIndex = PageIndex;
        QueryParameters.PageSize = PageSize;
        QueryParameters.QueryStrategy = QueryStrategy.Query;
        var uri = new Uri(_navigationManager.Uri);
        var uriBuilder = new UriBuilder(uri);
        var oldQuery = uriBuilder.Query;
        uriBuilder.Query = string.Empty;
        uri = uriBuilder.Uri;
        _currentQuery = $"?{QueryParameters}";
        if (force || (oldQuery != _currentQuery && !IsLoading)) _navigationManager.NavigateTo($"{uri}{_currentQuery}");
    }
}