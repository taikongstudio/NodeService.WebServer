using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using static System.Collections.Specialized.BitVector32;

namespace NodeService.WebServer.Services.NodeFileSystem
{
    internal class LambdaProgress<T> : IProgress<T>
    {
        readonly Action<T> _action;

        public LambdaProgress(Action<T> action)
        {
            _action = action;
        }

        public void Report(T value)
        {
            _action.Invoke(value);
        }
    }
}
