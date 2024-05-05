//using Google.Protobuf.WellKnownTypes;
//using Grpc.Core;

//using Microsoft.Extensions.Logging;
//using NodeService.WebServer.Services.VirtualSystem;
//using NodeService.Infrastructure.Services;
//using NodeService.Infrastructure.Messages;
//using NodeService.Infrastructure.Interfaces;


//namespace NodeService.WebServer.Services
//{
//    public class FileSystemServiceImpl : FileSystem.FileSystemBase
//    {
//        private readonly ILogger<FileSystemServiceImpl> _logger;
//        private readonly INodeServerService _nodeSessionService;
//        private readonly VirtualFileSystemConfig _virtualFileSystemConfig;

//        public FileSystemServiceImpl(
//            ILogger<FileSystemServiceImpl> logger,
//            INodeServerService nodeSessionService,
//            VirtualFileSystemConfig virtualFileSystemConfig)
//        {
//            _logger = logger;
//            _nodeSessionService = nodeSessionService;
//            _virtualFileSystemConfig = virtualFileSystemConfig;
//        }

//        public override async Task<FileSystemListDriveResponse> ListDrive(FileSystemListDriveRequest request, ServerCallContext context)
//        {

//            FileSystemListDriveResponse fileSystemListDriveRsp = new FileSystemListDriveResponse();
//            try
//            {
//                fileSystemListDriveRsp.RequestId = request.RequestId;
//                fileSystemListDriveRsp.NodeName = request.NodeName;
//                _logger.LogInformation($"{request}");
//                var rsp = await _nodeSessionService.SendMessageAsync<SubscribeEvent, FileSystemListDriveResponse>(request.NodeName, request,
//                    cancellationToken: context.CancellationToken
//                    );
//                if (rsp == null)
//                {
//                    throw new TimeoutException();
//                }
//                fileSystemListDriveRsp.ErrorCode = rsp.ErrorCode;
//                fileSystemListDriveRsp.Message = rsp.Message;
//                fileSystemListDriveRsp.Drives.AddRange(rsp.Drives);
//            }
//            catch (Exception ex)
//            {
//                fileSystemListDriveRsp.ErrorCode = ex.HResult;
//                fileSystemListDriveRsp.Message = ex.Message;
//                _logger.LogError($"NodeName:{request.NodeName}:{ex}");
//            }
//            _logger.LogInformation(fileSystemListDriveRsp.ToString());
//            return fileSystemListDriveRsp;
//        }

//        public async override Task<FileSystemListDirectoryResponse> ListDirectory(FileSystemListDirectoryRequest request, ServerCallContext context)
//        {
//            FileSystemListDirectoryResponse fileSystemListDirectoryRsp = new FileSystemListDirectoryResponse();
//            try
//            {
//                _logger.LogInformation($"{request}");
//                fileSystemListDirectoryRsp.RequestId = request.RequestId;
//                fileSystemListDirectoryRsp.NodeName = request.NodeName;
//                var rsp = await _inprocRpc.SendAsync<FileSystemListDirectoryResponse>(request.NodeName, request, cancellationToken: context.CancellationToken);
//                if (rsp == null)
//                {
//                    throw new TimeoutException();
//                }
//                fileSystemListDirectoryRsp.ErrorCode = rsp.ErrorCode;
//                fileSystemListDirectoryRsp.Message = rsp.Message;
//                fileSystemListDirectoryRsp.FileSystemObjects.AddRange(rsp.FileSystemObjects);
//            }
//            catch (Exception ex)
//            {
//                fileSystemListDirectoryRsp.ErrorCode = ex.HResult;
//                fileSystemListDirectoryRsp.Message = ex.Message;
//                _logger.LogError($"NodeName:{request.NodeName}:{ex}");
//            }
//            _logger.LogInformation(fileSystemListDirectoryRsp.ToString());
//            return fileSystemListDirectoryRsp;
//        }

//        public override async Task<FileSystemBulkOperationResponse> BulkOperaion(FileSystemBulkOperationRequest request, ServerCallContext context)
//        {
//            FileSystemBulkOperationResponse fileSystemBulkOperationRsp = new FileSystemBulkOperationResponse();
//            try
//            {
//                _logger.LogInformation($"{request}");
//                fileSystemBulkOperationRsp.RequestId = request.RequestId;
//                fileSystemBulkOperationRsp.NodeName = request.NodeName;
//                if (request.Operation == FileSystemOperation.Open)
//                {
//                    var httpContext = context.GetHttpContext();
//                    request.Headers.TryAdd("RequestUri", $"{_virtualFileSystemConfig.RequestUri}/api/virtualfilesystem/upload/{request.NodeName}");
//                }
//                var rsp = await _inprocRpc.SendAsync<FileSystemBulkOperationResponse>(request.NodeName, request, cancellationToken: context.CancellationToken);
//                if (rsp == null)
//                {
//                    throw new TimeoutException();
//                }
//                fileSystemBulkOperationRsp.ErrorCode = rsp.ErrorCode;
//                fileSystemBulkOperationRsp.Message = rsp.Message;
//            }
//            catch (Exception ex)
//            {
//                fileSystemBulkOperationRsp.ErrorCode = ex.HResult;
//                fileSystemBulkOperationRsp.Message = ex.Message;
//                _logger.LogError($"NodeName:{request.NodeName}:{ex}");
//            }
//            _logger.LogInformation(fileSystemBulkOperationRsp.ToString());
//            return fileSystemBulkOperationRsp;
//        }

//        public override Task<FileSystemBulkOperationCancelResponse> CancelBulkOperation(FileSystemBulkOperationCancelRequest request, ServerCallContext context)
//        {
//            return base.CancelBulkOperation(request, context);
//        }

//        public override async Task<FileSystemQueryBulkOperationReportResponse> QueryBulkOperationReport(FileSystemQueryBulkOperationReportRequest request, ServerCallContext context)
//        {
//            FileSystemQueryBulkOperationReportResponse rsp = new FileSystemQueryBulkOperationReportResponse();
//            try
//            {
//                _logger.LogInformation(request.ToString());
//                rsp.NodeName = request.NodeName;
//                rsp.RequestId = request.RequestId;
//                rsp.OriginalRequestId = request.OriginalRequestId;
//                using CancellationTokenSource cancellationTokenSource = new CancellationTokenSource();
//                if (request.Timeout > TimeSpan.Zero)
//                {
//                    cancellationTokenSource.CancelAfter(request.Timeout);
//                }
//                try
//                {
//                    await foreach (var reportMessage in _inprocMessageQueue.ReadAllAsync<FileSystemBulkOperationReport>(
//                            request.NodeName,
//                            (report) => report.RequestId == request.OriginalRequestId,
//                            cancellationTokenSource.Token))
//                    {
//                        rsp.Reports.Add(reportMessage);
//                    }
//                }
//                catch (OperationCanceledException ex)
//                {
//                    if (rsp.Reports.Count == 0)
//                    {
//                        rsp.ErrorCode = ex.HResult;
//                        rsp.Message = ex.Message;
//                    }
//                }
//                catch (Exception ex)
//                {
//                    _logger.LogError(ex.ToString());
//                    rsp.ErrorCode = ex.HResult;
//                    rsp.Message = ex.Message;
//                }
//            }
//            catch (Exception ex)
//            {
//                rsp.ErrorCode = ex.HResult;
//                rsp.Message = ex.Message;
//                _logger.LogError($"NodeName:{request.NodeName}:{ex}");
//            }

//            return rsp;
//        }


//    }
//}

