using Blazor.Diagrams.Core.Geometry;
using Blazor.Diagrams.Core.Models;
using Blazor.Diagrams.Core.Models.Base;
using NodeService.WebServer.UI.Pages.Tasks.Definitions.Diagrams.Models;

namespace NodeService.WebServer.UI.Pages.Tasks.Definitions.Diagrams
{
    public class TaskStageNode : GroupModel
    {
        public TaskStageNode(IEnumerable<TaskGroupNode> groups) : base(groups)
        {

        }

        public TaskStageNode(TaskStageNodeViewModel taskStageNodeModel) : base(
            [],
            (byte)taskStageNodeModel.Padding)
        {
            DataContext = taskStageNodeModel;
            Title = taskStageNodeModel.Name;
            SetPosition(taskStageNodeModel.X, taskStageNodeModel.Y);
            Size = new Size(taskStageNodeModel.Width, taskStageNodeModel.Height);
            SizeChanged += OnSizeChanged;
            AddPort(PortAlignment.Top);
            AddPort(PortAlignment.Bottom);
            InitTaskGroupNodes();
        }

        void InitTaskGroupNodes()
        {
            foreach (var group in this.DataContext.Groups)
            {
                var groupNode = new TaskGroupNode(group);
                this.AddChild(groupNode);
            }
        }

        public TaskStageNodeViewModel DataContext { get; private set; }

        public override bool CanAttachTo(ILinkable other)
        {
            return base.CanAttachTo(other);
        }

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
            DataContext.X += deltaX;
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
    }
}
