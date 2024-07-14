using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using static System.Collections.Specialized.BitVector32;

namespace NodeService.WebServer.Services.NodeFileSystem
{
    public class ProgressBlock<T> : IProgress<T>, IAsyncDisposable
    {
        readonly ActionBlock<T> _actionBlock;
        private readonly Func<T, CancellationToken,ValueTask> _func;

        public ProgressBlock(Func<T, CancellationToken,ValueTask> func)
        {
            _actionBlock = new ActionBlock<T>(InvokeAsync, new ExecutionDataflowBlockOptions()
            {
                EnsureOrdered = true,
                MaxDegreeOfParallelism = 1
            });

            _func = func;
        }

        async Task InvokeAsync(T item)
        {
            await _func.Invoke(item, default);
        }

        public void Report(T value)
        {
            _actionBlock.Post(value);
        }

        public async ValueTask DisposeAsync()
        {
            _actionBlock.Complete();
            await _actionBlock.Completion;
        }
    }
}
