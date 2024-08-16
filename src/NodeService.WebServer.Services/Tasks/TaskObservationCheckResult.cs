namespace NodeService.WebServer.Services.Tasks
{
    public class TaskObservationCheckResult
    {
        public string Id { get; set; }

        public string DisplayName { get; set; }

        public NodeInfoModel? NodeInfo { get; set; }

        public string Name { get; set; }

        public string CreationDateTime { get; set; }

        public string Status { get; set; }

        public string Message { get; set; }

        public string Solution { get; set; }

    }
}
