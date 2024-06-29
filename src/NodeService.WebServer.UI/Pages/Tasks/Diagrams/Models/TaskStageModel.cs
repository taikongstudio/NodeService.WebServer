using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace NodeService.WebServer.UI.Pages.Tasks.Diagrams.Models
{
    public class TaskStageModel
    {
        public string Name { get; set; }

        public List<TaskGroupModel> TaskGroups { get; set; } = [];
    }
}
