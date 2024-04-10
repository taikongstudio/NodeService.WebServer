using NodeService.Infrastructure.Logging;

namespace NodeService.WebServer.Models
{
    public struct LogPersistenceGroup
    {
        public LogPersistenceGroup(string id)
        {
            this.Id = id;
        }

        public string Id { get; private set; }
        public List<IEnumerable<LogEntry>> EntriesList { get; private set; } = [];

        public int TotalEntiresCount { get; set; }
    }
}
