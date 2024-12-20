﻿using NodeService.Infrastructure.NodeFileSystem;
using System;
using System.Collections.Generic;
using System.IO.Pipelines;
using System.Linq;
using System.Reactive.Disposables;
using System.Text;
using System.Threading.Tasks;

namespace NodeService.WebServer.Services.NodeFileSystem
{
    public static class NodeFileSyncRequestExtensions
    {
        public static NodeFileSyncRecordModel CreateNodeFileSyncRecord(this NodeFileSyncRequest nodeFileSyncRequest)
        {
            var record = new NodeFileSyncRecord()
            {
                StoragePath = nodeFileSyncRequest.StoragePath,
                ConfigurationId = nodeFileSyncRequest.ConfigurationId,
                ConfigurationProtocol = nodeFileSyncRequest.ConfigurationProtocol,
                FileInfo = NodeFileInfoSnapshot.From(nodeFileSyncRequest.FileInfo),
            };
            return new NodeFileSyncRecordModel()
            {
                Id = Guid.NewGuid().ToString(),
                NodeInfoId = nodeFileSyncRequest.NodeInfoId,
                ContextId = nodeFileSyncRequest.ContextId,
                Name = nodeFileSyncRequest.FileInfo.FullName,
                Value = record,
                Status = NodeFileSyncStatus.Pendding,
                FullName = nodeFileSyncRequest.FileInfo.FullName,
                Length = nodeFileSyncRequest.FileInfo.Length,
            };
        }

        public static NodeFileSyncContext CreateNodeFileUploadContext(
                                this NodeFileSyncRequest request,
                                Stream stream)
        {
            var syncRecord = CreateNodeFileSyncRecord(request);
            return new NodeFileSyncContext(
                request,
                syncRecord,
                stream);
        }

    }
}
