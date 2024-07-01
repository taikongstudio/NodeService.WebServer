using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Blazor.Diagrams.Core.Models;
using NodeService.Infrastructure.DataModels;


namespace NodeService.WebServer.UI.Pages.TaskFlows.Designer
{
    public class TaskFlowTemplateNode : NodeModel
    {
        public TaskFlowTemplateNode()
        {
        }

        public TaskFlowTemplate Template { get; set; }

        public TaskFlowExecutionInstanceModel? TaskFlowExecutionInstance { get; set; }
    }
}
