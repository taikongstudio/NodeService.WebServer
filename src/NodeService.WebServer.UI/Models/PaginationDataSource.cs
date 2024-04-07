using AntDesign;
using NodeService.Infrastructure;
using NodeService.Infrastructure.DataModels;
using NodeService.Infrastructure.Models;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace NodeService.WebServer.UI.Models
{
    public class PaginationDataSource<T>
        where T : ModelBase, new()
    {
        private readonly ApiService _apiService;

        public PaginationDataSource(ApiService apiService)
        {
            _apiService = apiService;

        }

        public int PageIndex { get; set; } = 1;

        public int TotalCount { get; set; }

        public int PageSize { get; set; } = 20;

        public IEnumerable<T> Items { get; private set; } = [];

        public IEnumerable<T> ItemsSource { get; private set; } = [];

        public Func<T, bool> Filter { get; set; }

        public void SetItemsSource(IEnumerable<T> itemsSource)
        {
            ItemsSource = itemsSource;
        }

        public void Refresh()
        {
            if (this.Filter != null)
            {
                this.Items = this.ItemsSource.Where(Filter);
            }
            else
            {
                this.Items = this.ItemsSource;
            }
            this.TotalCount = this.Items.Count();
        }

        public async Task UpdateSourceAsync(CancellationToken cancellationToken = default)
        {
            var pageApiResponse = await _apiService.QueryConfigurationListAsync<T>(
                QueryParameters.All,
                cancellationToken);
            this.SetItemsSource(pageApiResponse.Result);
            this.Refresh();
            this.TotalCount = pageApiResponse.TotalCount;

        }

        public async Task OnPageSizeChanged(PaginationEventArgs e)
        {
            this.PageIndex = e.Page;
            this.PageSize = e.PageSize;
            await this.UpdateSourceAsync();
        }

    }

}
