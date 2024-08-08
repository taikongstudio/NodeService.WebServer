using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace NodeService.WebServer.Models
{
    public class RedisOptions
    {
        public string EndPoints { get; set; }

        public string Password { get; set; }

        public string Channel { get; set; }
    }
}
