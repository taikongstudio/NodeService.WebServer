using Blazor.Diagrams.Core.Geometry;
using Blazor.Diagrams.Core.Models;
using NodeService.WebServer.UI.Pages.Tasks.Definitions.Diagrams.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading.Tasks;

namespace NodeService.WebServer.UI.Pages.Tasks.Definitions.Diagrams
{
    public class TaskNode : NodeModel
    {
        public TaskNode(TaskNodeViewModel taskNodeModel) : base(
            taskNodeModel.Id,
            new Point(
                taskNodeModel.X,
                taskNodeModel.Y))
        {
            DataContext = taskNodeModel;
            SetPosition(taskNodeModel.X, taskNodeModel.Y);
            Size = new Size(taskNodeModel.Width, taskNodeModel.Height);
            SizeChanged += OnSizeChanged;
        }

        public TaskNodeViewModel DataContext { get; private set; }

        public override void SetPosition(double x, double y)
        {
            DataContext.X = x;
            DataContext.Y = y;
            base.SetPosition(x, y);
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

        ~TaskNode()
        {
            SizeChanged -= OnSizeChanged;
        }

        private TaskGroupNode _taskGroupNode;

        public void SetGroup(TaskGroupNode taskGroupNode)
        {
            ArgumentNullException.ThrowIfNull(taskGroupNode);
            if (_taskGroupNode == taskGroupNode)
            {
                return;
            }
            else
            {
                _taskGroupNode.RemoveChild(this);
            }
            _taskGroupNode = taskGroupNode;
            _taskGroupNode.AddChild(this);
        }

        public TaskGroupNode GetGroup()
        {
            return _taskGroupNode;
        }


    }
}
