using NodeService.WebServer.Models;
using NodeService.WebServer.Services.VirtualSystem;

namespace NodeService.WebServer.Controllers
{
    [ApiController]
    [Route("api/[controller]/[action]")]
    public class VirtualFileSystemController : Controller
    {
        private readonly IVirtualFileSystem _virtualFileSystem;
        private readonly WebServerOptions _webServerOptions;
        private readonly IConfiguration _configuration;

        public VirtualFileSystemController(
            IVirtualFileSystem virtualFileSystem,
            WebServerOptions webServerOptions,
            IConfiguration configuration)
        {
            _virtualFileSystem = virtualFileSystem;
            _webServerOptions = webServerOptions;
            _configuration = configuration;
        }

        [HttpGet("/api/virtualfilesystem/{**path}")]
        public async Task<IActionResult> DownloadAsync(string path)
        {
            await _virtualFileSystem.ConnectAsync();
            if (!await _virtualFileSystem.FileExits(path))
            {
                return NotFound();
            }
            var memoryStream = new MemoryStream();
            if (await _virtualFileSystem.DownloadStream(path, memoryStream))
            {
                memoryStream.Position = 0;
                return File(memoryStream, "application/octet-stream");
            }
            return NotFound();
        }

        //[ValidateAntiForgeryToken]
        [DisableRequestSizeLimit()]
        [HttpPost("/api/virtualfilesystem/upload/{nodeName}")]
        public async Task<IActionResult> OnPostUploadAsync(string nodeName, List<IFormFile> files)
        {
            ApiResponse<UploadFileResult> uploadFileResult = new ApiResponse<UploadFileResult>()
            {
                Result = new UploadFileResult()
            };
            try
            {
                uploadFileResult.Result.UploadedFiles = new List<UploadedFile>();
                var nodeCachePath = _webServerOptions.GetFileCachesPath(nodeName);
                foreach (var formFile in files)
                {
                    var fileId = formFile.Headers["FileId"];
                    var remotePath = Path.Combine(nodeCachePath, Guid.NewGuid().ToString()).Replace("\\", "/");
                    var downloadUrl = $"{_configuration.GetValue<string>("Kestrel:Endpoints:MyHttpEndpoint:Url")}/api/virtualfilesystem/{remotePath}";
                    if (await _virtualFileSystem.UploadStream(
                        remotePath, formFile.OpenReadStream()))
                    {
                        uploadFileResult.Result.UploadedFiles.Add(new UploadedFile()
                        {
                            DownloadUrl = downloadUrl,
                            Name = formFile.FileName,
                            FileId = fileId
                        });
                    }

                }
            }
            catch (Exception ex)
            {
                uploadFileResult.ErrorCode = ex.HResult;
                uploadFileResult.Message = ex.Message;
            }

            return new JsonResult(uploadFileResult);
        }


    }
}
