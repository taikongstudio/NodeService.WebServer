using AntDesign.ProLayout;

namespace NodeService.WebServer.UI.Models
{
    public class NoticeItem : NoticeIconData
    {
        public string Id { get; set; }
        public string Type { get; set; }
        public string Status { get; set; }
    }
}