using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace NodeService.WebServer.UI.Pages.Tasks.Diagrams.Models
{
    public class TaskDefinitionModel
    {
        public TaskDefinitionModel()
        {

        }

        public string Name { get; set; }

        public TaskStageModel TriggerStage { get; set; }

        public List<TaskStageModel> TaskStages { get; set; } = [];

    }
}
