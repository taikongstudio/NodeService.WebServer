using Blazor.Diagrams.Core.Geometry;
using Blazor.Diagrams.Core.Models;

namespace NodeService.WebServer.UI.Diagrams.Demos;

public class BotAnswerNode : NodeModel
{
    public BotAnswerNode(Point position = null) : base(position) { }

    public string Answer { get; set; }
}
