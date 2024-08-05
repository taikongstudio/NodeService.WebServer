using Confluent.Kafka;

namespace NodeService.WebServer.Services.TaskSchedule
{
    public class KafkaDelayMessageConsumeContext
    {
        public IProducer<string, string> Producer { get; init; }

        public ConsumeResult<string, string> Result { get; init; }
    }
}
