using Microsoft.AspNetCore.Mvc;

namespace NodeService.WebServer.Models
{
    public class QueryParametersModel
    {
        [FromQuery]
        public int PageSize { get; set; }
        [FromQuery]
        public int PageIndex { get; set; }

    }
}
