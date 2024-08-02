namespace NodeService.WebServer.Services.NodeSessions
{
    record struct AnalysisPropsResult
    {
        public AnalysisPropsResult()
        {
        }

        public AnalysisProcessListResult ProcessListResult { get; set; }

        public AnalysisServiceProcessListResult ServiceProcessListResult { get; set; }

    }
}
