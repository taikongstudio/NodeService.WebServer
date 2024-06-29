using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace NodeService.WebServer.UI.Pages.Tasks.Diagrams.Models
{
    public class TaskGroupModel
    {
        public TaskGroupModel()
        {

        }
        public string Name { get; set; }

        public List<NodeModel> Nodes { get; set; } = [];
        public List<TaskModel> Tasks { get; set; } = [];
    }
}
