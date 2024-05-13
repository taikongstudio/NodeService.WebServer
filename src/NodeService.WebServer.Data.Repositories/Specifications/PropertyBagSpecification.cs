using Ardalis.Specification;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace NodeService.WebServer.Data.Repositories.Specifications
{
    public class PropertyBagSpecification : Specification<PropertyBag>
    {
        public PropertyBagSpecification(string id)
        {
            if (!string.IsNullOrEmpty(id))
            {
                Query.Where(x => x["Id"] == (object)id);
            }
        }
    }
}
