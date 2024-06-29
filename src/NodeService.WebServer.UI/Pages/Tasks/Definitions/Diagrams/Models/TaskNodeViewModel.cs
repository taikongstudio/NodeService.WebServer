using NodeService.Infrastructure.Models;
using NodeService.WebServer.UI.Pages.Tasks.Definitions.Diagrams;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace NodeService.WebServer.UI.Pages.Tasks.Definitions.Diagrams.Models
{
    public class TaskNodeViewModel : NodeViewModelBase
    {
        public TaskNodeViewModel(double x, double y)
        {
            X = x;
            Y = y;
        }

        public TaskNodeViewModel()
        {
        }

        public TaskNode CreateNode()
        {
            return new TaskNode(this);
        }

        public TaskExecutionStatus Status { get; set; }
    }
}
