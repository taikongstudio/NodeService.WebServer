using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace NodeService.WebServer.Models
{
    public class XlsxAttachment : EmailAttachmentBase
    {
        public XlsxAttachment(
            string name,
            Stream stream)
        {
            Name = name;
            Stream = stream;
            MediaType = "application";
            MediaSubtype = "vnd.openxmlformats-officedocument.spreadsheetml.sheet";
        }
    }
}
