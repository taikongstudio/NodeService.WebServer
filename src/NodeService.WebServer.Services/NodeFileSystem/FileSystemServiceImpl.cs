using Google.Protobuf.WellKnownTypes;
using Grpc.Core;
using Microsoft.Extensions.Logging;
using NodeService.Infrastructure.Services;
using NodeService.Infrastructure.Messages;
using NodeService.Infrastructure.Interfaces;
using NodeService.Infrastructure.NodeSessions;
using NodeService.WebServer.Services.VirtualFileSystem;


namespace NodeService.WebServer.Services.NodeFileSystem;

public class FileSystemServiceImpl : NodeFileSystemService.NodeFileSystemServiceBase
{
    private readonly ILogger<FileSystemServiceImpl> _logger;
    private readonly INodeSessionService _nodeSessionService;

    public FileSystemServiceImpl(
        ILogger<FileSystemServiceImpl> logger,
        INodeSessionService nodeSessionService,
        IVirtualFileSystem virtualFileSystem)
    {
        _logger = logger;
        _nodeSessionService = nodeSessionService;
    }

    public override Task<FileSystemListDriveResponse> ListDrive(FileSystemListDriveRequest request, ServerCallContext context)
    {
        return base.ListDrive(request, context);
    }

    public override Task<FileSystemListDirectoryResponse> ListDirectory(FileSystemListDirectoryRequest request, ServerCallContext context)
    {
        return base.ListDirectory(request, context);
    }

}