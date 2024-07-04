using Microsoft.Extensions.FileProviders;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace NodeService.WebServer.Services.NodeFileSystem
{
    public class NodeVirtualFileInfo : IFileInfo
    {
        public bool Exists => true;

        public bool IsDirectory => true;

        public DateTimeOffset LastModified => DateTime.Now;

        public long Length => 0;

        public string Name => "Test";

        public string? PhysicalPath => "123";

        public Stream CreateReadStream()
        {
            return Stream.Null;
        }
    }
}
