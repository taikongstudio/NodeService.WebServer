using Blazor.Diagrams.Core.Models;

namespace NodeService.WebServer.UI.Diagrams.Demos.CustomLink;

public class ThickLink : LinkModel
{
    public ThickLink(PortModel sourcePort, PortModel targetPort = null) : base(sourcePort, targetPort) { }
}
