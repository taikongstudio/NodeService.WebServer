using Google.Protobuf.WellKnownTypes;
using Grpc.Core;

using Microsoft.Extensions.Logging;
using NodeService.WebServer.Services.VirtualSystem;
using NodeService.Infrastructure.Services;
using NodeService.Infrastructure.Messages;
using NodeService.Infrastructure.Interfaces;
using NodeService.Infrastructure.NodeSessions;
using static NodeService.Infrastructure.Services.FileSystem;



namespace NodeService.WebServer.Services
{
    public class FileSystemServiceImpl : FileSystemBase
    {
        readonly ILogger<FileSystemServiceImpl> _logger;
        readonly INodeSessionService _nodeSessionService;
        readonly IVirtualFileSystem _virtualFileSystem;

        public FileSystemServiceImpl(
            ILogger<FileSystemServiceImpl> logger,
            INodeSessionService nodeSessionService,
            IVirtualFileSystem virtualFileSystem)
        {
            _logger = logger;
            _nodeSessionService = nodeSessionService;
            _virtualFileSystem = virtualFileSystem;
        }

        public override async Task<FileSystemListDriveResponse> ListDrive(
            FileSystemListDriveRequest request,
            ServerCallContext context)
        {

            FileSystemListDriveResponse fileSystemListDriveRsp = new FileSystemListDriveResponse();
            try
            {
                var httpContext = context.GetHttpContext();
                var nodeClientHeaders = context.RequestHeaders.GetNodeClientHeaders();
                var nodeSessionId = new NodeSessionId(nodeClientHeaders.NodeId);
                fileSystemListDriveRsp.RequestId = request.RequestId;
                var rsp = await _nodeSessionService.SendMessageAsync<SubscribeEvent, FileSystemListDriveResponse>(nodeSessionId,
                    new SubscribeEvent()
                    {
                        RequestId = request.RequestId,
                        Timeout = TimeSpan.FromSeconds(30),
                        FileSystemListDriveRequest = request,
                        Topic = "FileSystem"
                    },
                    context.CancellationToken
                    );
                fileSystemListDriveRsp = rsp;
            }
            catch (Exception ex)
            {
                fileSystemListDriveRsp.ErrorCode = ex.HResult;
                fileSystemListDriveRsp.Message = ex.Message;
            }
            return fileSystemListDriveRsp;
        }

        public async override Task<FileSystemListDirectoryResponse> ListDirectory(
            FileSystemListDirectoryRequest request,
            ServerCallContext context)
        {
            FileSystemListDirectoryResponse fileSystemListDirectoryRsp = new FileSystemListDirectoryResponse();
            try
            {
                var httpContext = context.GetHttpContext();
                var nodeClientHeaders = context.RequestHeaders.GetNodeClientHeaders();
                var nodeSessionId = new NodeSessionId(nodeClientHeaders.NodeId);
                fileSystemListDirectoryRsp.RequestId = request.RequestId;
                var rsp = await _nodeSessionService.SendMessageAsync<SubscribeEvent, FileSystemListDirectoryResponse>(nodeSessionId,
                    new SubscribeEvent()
                    {
                        RequestId = request.RequestId,
                        Timeout = TimeSpan.FromSeconds(30),
                        FileSystemListDirectoryRequest = request,
                        Topic = "FileSystem"
                    },
                    context.CancellationToken
                    );
                fileSystemListDirectoryRsp = rsp;
            }
            catch (Exception ex)
            {
                fileSystemListDirectoryRsp.ErrorCode = ex.HResult;
                fileSystemListDirectoryRsp.Message = ex.Message;
            }
            return fileSystemListDirectoryRsp;
        }

        public override async Task<FileSystemBulkOperationResponse> BulkOperaion(FileSystemBulkOperationRequest request, ServerCallContext context)
        {
            FileSystemBulkOperationResponse fileSystemOperationRsp = new FileSystemBulkOperationResponse();
            try
            {
                var httpContext = context.GetHttpContext();
                var nodeClientHeaders = context.RequestHeaders.GetNodeClientHeaders();
                var nodeSessionId = new NodeSessionId(nodeClientHeaders.NodeId);
                if (request.Operation == FileSystemOperation.Open)
                {
                    //request.Headers.TryAdd("RequestUri", $"{_virtualFileSystemConfig.RequestUri}/api/virtualfilesystem/upload/{request.NodeName}");
                }
                var rsp = await _nodeSessionService.SendMessageAsync<FileSystemBulkOperationRequest, FileSystemBulkOperationResponse>(nodeSessionId, request, cancellationToken: context.CancellationToken);
                fileSystemOperationRsp = rsp;
            }
            catch (Exception ex)
            {
                fileSystemOperationRsp.ErrorCode = ex.HResult;
                fileSystemOperationRsp.Message = ex.Message;
            }
            return fileSystemOperationRsp;
        }

        public override async Task<FileSystemQueryBulkOperationReportResponse> QueryBulkOperationReport(FileSystemQueryBulkOperationReportRequest request, ServerCallContext context)
        {
            FileSystemQueryBulkOperationReportResponse rsp = new FileSystemQueryBulkOperationReportResponse();
            try
            {
                var httpContext = context.GetHttpContext();
                var nodeClientHeaders = context.RequestHeaders.GetNodeClientHeaders();
                var nodeSessionId = new NodeSessionId(nodeClientHeaders.NodeId);
                rsp.RequestId = request.RequestId;
                rsp.OriginalRequestId = request.OriginalRequestId;
                await foreach (var reportMessage in _nodeSessionService.GetInputQueue(nodeSessionId).ReadAllAsync(context.CancellationToken))
                {

                }
            }
            catch (Exception ex)
            {
                rsp.ErrorCode = ex.HResult;
                rsp.Message = ex.Message;
            }

            return rsp;
        }

        public override Task<FileSystemBulkOperationCancelResponse> CancelBulkOperation(FileSystemBulkOperationCancelRequest request, ServerCallContext context)
        {
            return base.CancelBulkOperation(request, context);
        }


    }
}

