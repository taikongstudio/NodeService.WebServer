using NodeService.Infrastructure.DataModels;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace NodeService.WebServer.UI.Pages.Tasks.Definitions.Diagrams.Models
{
    public class TaskGroupNodeViewModel : NodeViewModelBase
    {
        public TaskGroupRunningMode RunningMode { get; set; }


        public List<TaskNodeViewModel> Tasks { get; set; } = [];

      

    }
}
