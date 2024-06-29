using NodeService.WebServer.UI.Pages.Tasks.Definitions.Diagrams;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace NodeService.WebServer.UI.Pages.Tasks.Definitions.Diagrams.Models
{
    public class TaskStageNodeViewModel : NodeViewModelBase
    {
        public List<TaskGroupNodeViewModel> Groups { get; set; } = [];

    }
}
