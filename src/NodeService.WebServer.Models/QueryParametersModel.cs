using Microsoft.AspNetCore.Mvc;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace NodeService.WebServer.Models
{
    public class QueryParametersModel
    {
        [FromQuery]
        public int PageSize {  get; set; }
        [FromQuery]
        public int PageIndex {  get; set; }

    }
}
