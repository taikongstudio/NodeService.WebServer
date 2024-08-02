using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace NodeService.WebServer.Models
{
    public class EmailAttachment
    {
        public EmailAttachment(
            string name,
            string medieType,
            string mediaSubtype,
            Stream stream)
        {
            Name = name;
            Stream = stream;
            MediaType = medieType;
            MediaSubtype = mediaSubtype;
        }

        public string Name { get; private set; }

        public Stream Stream { get; private set; }

        public string MediaType { get; private set; }

        public string MediaSubtype { get; private set; }

    }
}
