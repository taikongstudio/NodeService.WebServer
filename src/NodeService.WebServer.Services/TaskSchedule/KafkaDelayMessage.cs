namespace NodeService.WebServer.Services.TaskSchedule
{
    public record class KafkaDelayMessage
    {
        public string Type { get; set; }

        public string SubType { get; set; }

        public string Id { get; set; }

        public DateTime CreateDateTime { get; set; }

        public DateTime ScheduleDateTime { get; set; }

        public TimeSpan Duration { get; set; }

        public Dictionary<string, string> Properties { get; set; } = [];

        public bool Handled { get; set; }

    }
}
