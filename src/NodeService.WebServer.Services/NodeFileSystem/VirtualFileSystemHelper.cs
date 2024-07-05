using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace NodeService.WebServer.Services.NodeFileSystem
{
    public static class VirtualFileSystemHelper
    {
        public static string GetRootDirectory()
        {
            return Path.Combine(AppContext.BaseDirectory, "..", "VFS_Root");
        }

        public static string GetNodeRoot(string nodeName)
        {
            return Path.Combine(GetRootDirectory(), nodeName);
        }
    }
}
