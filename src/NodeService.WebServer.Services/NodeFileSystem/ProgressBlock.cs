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
        readonly ActionBlock<T[]> _actionBlock;
        readonly Func<T[], CancellationToken,ValueTask> _func;
        readonly Timer _timer;
        readonly BatchBlock<T> _batchBlock;
        readonly IDisposable _token;

        public ProgressBlock(Func<T[], CancellationToken, ValueTask> func)
        {
            _timer = new Timer(OnTick, null, 500, 1000);
            _batchBlock = new BatchBlock<T>(2048, new GroupingDataflowBlockOptions()
            {
                EnsureOrdered = true
            });

            _actionBlock = new ActionBlock<T[]>(InvokeAsync, new ExecutionDataflowBlockOptions()
            {
                EnsureOrdered = true,
                MaxDegreeOfParallelism = 1
            });

            _token =   _batchBlock.LinkTo(_actionBlock, new DataflowLinkOptions()
            {
                PropagateCompletion = true
            });


            _func = func;
        }

        void OnTick(object? state)
        {
            _batchBlock.TriggerBatch();
        }

        async Task InvokeAsync(T[] item)
        {
            await _func.Invoke(item, default);
        }

        public void Report(T value)
        {
            _batchBlock.Post(value);
        }

        public async ValueTask DisposeAsync()
        {
            await _timer.DisposeAsync();
            _batchBlock.Complete();
            await _batchBlock.Completion;
            _actionBlock.Complete();
            await _actionBlock.Completion;
            _token.Dispose();
        }
    }
}
