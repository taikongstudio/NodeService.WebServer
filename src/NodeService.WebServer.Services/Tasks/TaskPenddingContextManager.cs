using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace NodeService.WebServer.Services.Tasks;

public interface ITaskPenddingContextManager
{
    bool TryGetContext(string id, out TaskPenddingContext? context);

    bool AddContext(TaskPenddingContext context);

    bool RemoveContext(string id, out TaskPenddingContext? context);
}

public class TaskPenddingContextManager : ITaskPenddingContextManager
{
    private ConcurrentDictionary<string, TaskPenddingContext> _contexts;

    public TaskPenddingContextManager()
    {
        _contexts = new ConcurrentDictionary<string, TaskPenddingContext>();
    }

    public bool AddContext(TaskPenddingContext context)
    {
        return _contexts.TryAdd(context.Id, context);
    }

    public bool RemoveContext(string id, out TaskPenddingContext? context)
    {
        return _contexts.TryRemove(id, out context);
    }

    public bool TryGetContext(string id, out TaskPenddingContext? context)
    {
        return _contexts.TryGetValue(id, out context);
    }
}