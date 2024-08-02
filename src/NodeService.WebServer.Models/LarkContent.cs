using NodeService.Infrastructure.DataModels;

namespace NodeService.WebServer.Models;

public class LarkContent
{
    public string Subject { get; init; }
    public IEnumerable<StringEntry> Entries { get; init; }
}
