using NodeService.WebServer.Extensions;

namespace NodeService.WebServer.Data.Repositories.Specifications
{
    public class TaskDefinitionSpecification : Specification<JobScheduleConfigModel>
    {
        public TaskDefinitionSpecification(
            string? keywords,
            IEnumerable<SortDescription>? sortDescriptions = null)
        {
            if (!string.IsNullOrEmpty(keywords))
            {
                Query.Where(x => x.Name.Contains(keywords));
            }
            if (sortDescriptions != null && sortDescriptions.Any())
            {
                Query.SortBy(sortDescriptions);
            }
        }

        public TaskDefinitionSpecification(
            bool isEnabled,
            JobScheduleTriggerType triggerType,
            IEnumerable<SortDescription>? sortDescriptions = null)
        {
            if (sortDescriptions != null && sortDescriptions.Any())
            {
                Query.SortBy(sortDescriptions);
            }
        }

    }
}
