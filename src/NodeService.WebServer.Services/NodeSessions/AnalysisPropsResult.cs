namespace NodeService.WebServer.Services.NodeSessions
{
    record struct AnalysisPropsResult
    {
        public AnalysisPropsResult()
        {
        }

        public AnalysisProcessListResult ProcessListResult { get; set; }

        public AnalysisServiceProcessListResult ServiceProcessListResult { get; set; }

        public ProcessInfo[] ProcessInfoList { get; set; } = [];

        public ServiceProcessInfo[] ServiceProcessInfoList { get; set; } = [];
    }
}
