namespace NodeService.WebServer.Services.NodeSessions
{
    record struct AnalysisProcessListResult
    {
        public AnalysisProcessListResult()
        {
        }

        public IEnumerable<string> Usages { get; set; } = [];

        public List<ProcessInfo> StatusChangeProcessList { get; set; } = [];
    }
}
