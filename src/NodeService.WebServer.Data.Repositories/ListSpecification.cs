using Ardalis.Specification;
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
        public ListSpecification() : this(null, default)
        {

        }

        public ListSpecification(ISpecification<T>? specification, DataFilterCollection<string> idFilters)
        {
            if (specification!=null)
            {
                CopySpecification(specification);
            }

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

        private void CopySpecification(ISpecification<T> specification)
        {
            //if (specification.Skip != null)
            //{
            //    this.Query.Skip(specification.Skip.Value);
            //}

            //if (specification.Take != null)
            //{
            //    this.Query.Take(specification.Take.Value);
            //}

            this.Query.AsNoTracking(specification.AsNoTracking);

            this.Query.AsNoTrackingWithIdentityResolution(specification.AsNoTrackingWithIdentityResolution);

            this.Query.AsSplitQuery(specification.AsSplitQuery);

            this.Query.AsTracking(specification.AsTracking);

            this.Query.IgnoreQueryFilters(specification.IgnoreQueryFilters);

            if (specification.PostProcessingAction != null)
            {
                this.Query.PostProcessingAction(specification.PostProcessingAction);
            }

            foreach (var searchExpr in specification.SearchCriterias)
            {
                this.Query.Search(searchExpr.Selector, searchExpr.SearchTerm, searchExpr.SearchGroup);
            }


            foreach (var whereExpr in specification.WhereExpressions)
            {
                this.Query.Where(whereExpr.Filter);
            }

            foreach (var orderExpr in specification.OrderExpressions)
            {
                switch (orderExpr.OrderType)
                {
                    case OrderTypeEnum.OrderBy:
                        this.Query.OrderBy(orderExpr.KeySelector);
                        break;
                    case OrderTypeEnum.OrderByDescending:
                        this.Query.OrderByDescending(orderExpr.KeySelector);
                        break;
                    case OrderTypeEnum.ThenBy:
                        break;
                    case OrderTypeEnum.ThenByDescending:
                        break;
                    default:
                        break;
                }

            }
        }

        public ListSpecification(DataFilterCollection<string> idFilters) : this(null, idFilters)
        {
        }


    }
}
