using NodeService.WebServer.Services.Counters;
using NodeService.WebServer.Services.VirtualFileSystem;

namespace NodeService.WebServer.Controllers;

[ApiController]
[Route("api/[controller]/[action]")]
public class VirtualFileSystemController : Controller
{
    private readonly IConfiguration _configuration;
    private readonly ExceptionCounter _exceptionCounter;
    private readonly ILogger<VirtualFileSystemController> _logger;
    private readonly IVirtualFileSystem _virtualFileSystem;
    private readonly WebServerOptions _webServerOptions;

    public VirtualFileSystemController(
        ILogger<VirtualFileSystemController> logger,
        ExceptionCounter exceptionCounter,
        IVirtualFileSystem virtualFileSystem,
        WebServerOptions webServerOptions,
        IConfiguration configuration)
    {
        _virtualFileSystem = virtualFileSystem;
        _webServerOptions = webServerOptions;
        _configuration = configuration;
        _logger = logger;
        _exceptionCounter = exceptionCounter;
    }

    [HttpGet("/api/virtualfilesystem/{**path}")]
    public async Task<IActionResult> DownloadAsync(string path)
    {
        await _virtualFileSystem.ConnectAsync();
        if (!await _virtualFileSystem.FileExits(path)) return NotFound();
        var memoryStream = new MemoryStream();
        if (await _virtualFileSystem.DownloadStream(path, memoryStream))
        {
            memoryStream.Position = 0;
            return File(memoryStream, "application/octet-stream");
        }

        return NotFound();
    }

    //[ValidateAntiForgeryToken]
    [DisableRequestSizeLimit]
    [HttpPost("/api/virtualfilesystem/upload/{nodeName}")]
    public async Task<IActionResult> OnPostUploadAsync(string nodeName, List<IFormFile> files)
    {
        var uploadFileResultRsp = new ApiResponse<UploadFileResult>();
        uploadFileResultRsp.SetResult(new UploadFileResult());
        try
        {
            uploadFileResultRsp.Result.UploadedFiles = new List<UploadedFile>();
            var nodeCachePath = _webServerOptions.GetFileCachesPath(nodeName);
            foreach (var formFile in files)
            {
                var fileId = formFile.Headers["FileId"];
                var remotePath = Path.Combine(nodeCachePath, Guid.NewGuid().ToString()).Replace("\\", "/");
                var downloadUrl =
                    $"{_configuration.GetValue<string>("Kestrel:Endpoints:MyHttpEndpoint:Url")}/api/virtualfilesystem/{remotePath}";
                if (await _virtualFileSystem.UploadStream(
                        remotePath, formFile.OpenReadStream()))
                    uploadFileResultRsp.Result.UploadedFiles.Add(new UploadedFile
                    {
                        Path = remotePath,
                        FileId = fileId
                    });
            }
        }
        catch (Exception ex)
        {
            _exceptionCounter.AddOrUpdate(ex);
            _logger.LogError(ex.ToString());
            uploadFileResultRsp.ErrorCode = ex.HResult;
            uploadFileResultRsp.Message = ex.Message;
        }

        return new JsonResult(uploadFileResultRsp);
    }
}