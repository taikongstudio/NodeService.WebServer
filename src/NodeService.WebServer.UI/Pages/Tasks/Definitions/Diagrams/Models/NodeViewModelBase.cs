namespace NodeService.WebServer.UI.Pages.Tasks.Definitions.Diagrams.Models
{
    public class NodeViewModelBase
    {
        public string Id { get; set; }

        public string Name { get; set; }
        public double X { get; set; }

        public double Y { get; set; }

        public double Width { get; set; }

        public double Height { get; set; }

        public int Padding { get; set; } = 30;

        public bool IsLocked { get; set; }
    }
}