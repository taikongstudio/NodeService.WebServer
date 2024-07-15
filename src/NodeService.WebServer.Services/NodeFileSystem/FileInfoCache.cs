using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace NodeService.WebServer.Data.Entities
{
    public class FileInfoCache
    {

        public string FullName { get; set; }

        public long Length { get; set; }

        public DateTime DateTime { get; set; }

        public DateTime CreateDateTime { get; set; }

        public DateTime ModifiedDateTime { get; set; }
    }
}
