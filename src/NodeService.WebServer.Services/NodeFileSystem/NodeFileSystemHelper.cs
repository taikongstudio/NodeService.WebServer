﻿using NodeService.Infrastructure.NodeSessions;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;

namespace NodeService.WebServer.Services.NodeFileSystem
{
    public static class NodeFileSystemHelper
    {
        public static string GetNodeFilePath(string nodeName, string filePath)
        {
            return $"NodeFileSystem://{nodeName}//{filePath}";
        }

        public static string GetNodeFilePathHash(string nodeFilePath)
        {
            var fileIdHashBytes = SHA256.HashData(Encoding.UTF8.GetBytes(nodeFilePath));
            var nodeFilePathHash = BitConverter.ToString(fileIdHashBytes).ToLowerInvariant();
            return nodeFilePathHash;
        }
    }
}
