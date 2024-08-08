namespace NodeService.WebServer.Models
{
    public class EmailAttachmentBase
    {

        public string MediaSubtype { get; protected set; }

        public string MediaType { get; protected set; }

        public string Name { get; protected set; }

        public Stream Stream { get; protected set; }
    }
}