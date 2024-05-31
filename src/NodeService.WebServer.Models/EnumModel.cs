using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace NodeService.WebServer.Models
{
    public class EnumModel<T> where T : Enum
    {
        public T Value { get; set; }

        public string Name { get; set; }
    }
}
