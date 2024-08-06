using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace NodeService.WebServer.Services.Tasks
{
    public class TaskObservationEvent
    {
        public string Id { get; set; }

        public string Name { get; set; }

        public string Type { get; set; }

        public int Status { get; set; }

        public string Context { get; set; }

        public string Message { get; set; }

        public DateTime CreationDateTime { get; set; }
    }
}
