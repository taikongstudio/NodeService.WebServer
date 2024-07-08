using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace NodeService.WebServer.Data.Repositories
{
    public class ListSpecification<T> : Specification<T>
        where T : EntityBase
    {
        public ListSpecification()
        {
        }

        public ListSpecification(DataFilterCollection<string> idFilters)
        {
            if (idFilters.HasValue)
            {
                switch (idFilters.FilterType)
                {
                    case DataFilterTypes.None:
                        break;
                    case DataFilterTypes.Include:
                       Query.Where(x => idFilters.Items.Contains(x.Id));
                        break;
                    case DataFilterTypes.Exclude:
                        Query.Where(x => idFilters.Items.Contains(x.Id));
                        break;
                    default:
                        break;
                }


            }

        }


    }
}
