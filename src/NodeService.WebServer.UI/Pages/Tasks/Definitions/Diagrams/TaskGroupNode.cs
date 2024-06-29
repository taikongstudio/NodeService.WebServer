using Blazor.Diagrams.Core.Geometry;
using Blazor.Diagrams.Core.Models;
using NodeService.WebServer.UI.Pages.Tasks.Definitions.Diagrams.Models;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;

namespace NodeService.WebServer.UI.Pages.Tasks.Definitions.Diagrams
{
    public class TaskGroupNode : GroupModel
    {
        public TaskGroupNode(IEnumerable<TaskNode> taskNodes) : base(taskNodes)
        {

        }

        public TaskGroupNode(TaskGroupNodeViewModel taskGroupNodeModel) : base([], (byte)taskGroupNodeModel.Padding)
        {
            DataContext = taskGroupNodeModel;
            SetPosition(taskGroupNodeModel.X, taskGroupNodeModel.Y);
            Size = new Size(taskGroupNodeModel.Width, taskGroupNodeModel.Height);
            SizeChanged += OnSizeChanged;
             this.InitTaskNodes();
        }

        void InitTaskNodes()
        {
            foreach (var taskNodeModel in this.DataContext.Tasks)
            {
                var taskNode = new TaskNode(taskNodeModel);
                taskNode.SetGroup(this);
                this.AddChild(taskNode);
            }
        }

        public override void Refresh()
        {

            base.Refresh();
        }



        public TaskGroupNodeViewModel DataContext { get; private set; }

        public override void SetPosition(double x, double y)
        {
            DataContext.X = x;
            DataContext.Y = y;
            base.SetPosition(x, y);
        }

        public void SetTitle(string title)
        {
            Title = title;
            DataContext.Name = title;
        }

        public override void UpdatePositionSilently(double deltaX, double deltaY)
        {
            DataContext.Y += deltaX;
            DataContext.Y += deltaY;
            base.UpdatePositionSilently(deltaX, deltaY);
        }

        private void OnSizeChanged(NodeModel nodeModel)
        {
            if (Size == null)
            {
                return;
            }
            DataContext.Width = Size!.Width;
            DataContext.Height = Size!.Height;
        }

        private TaskStageNode _taskStageNode;

        public void SetStage(TaskStageNode taskStageNode)
        {
            ArgumentNullException.ThrowIfNull(taskStageNode);
            if (_taskStageNode == taskStageNode)
            {
                return;
            }
            else
            {
                _taskStageNode.RemoveChild(this);
            }
            _taskStageNode = taskStageNode;
            _taskStageNode.AddChild(this);
        }

        public TaskStageNode GetGroup()
        {
            return _taskStageNode;
        }


    }
}
