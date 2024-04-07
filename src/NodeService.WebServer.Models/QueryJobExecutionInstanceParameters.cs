using Microsoft.AspNetCore.Mvc;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using static NodeService.Infrastructure.Models.JobExecutionReport.Types;

namespace NodeService.WebServer.Models
{
    public class QueryJobExecutionInstanceParameters
    {
        [FromQuery]
        public DateTime? BeginDateTime { get; set; }

        [FromQuery]
        public DateTime? EndDateTime { get; set; }

        [FromQuery]
        public string? JobScheduleConfigId { get; set; }

        [FromQuery]
        public string? NodeId { get; set; }

        [FromQuery]
        public JobExecutionStatus? Status { get; set; }
    }
}
