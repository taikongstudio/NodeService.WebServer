﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace NodeService.WebServer.UI.Pages.TaskFlows.Designer.Models
{
    public class TaskFlowDesignModel : TaskFlowDesignModelBase
    {
        public List<TaskFlowStageDesignModel> Stages { get; set; } = [];
    }
}
