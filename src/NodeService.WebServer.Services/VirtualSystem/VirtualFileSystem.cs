using FluentFTP;
using System.Runtime.CompilerServices;
using System.Threading.Channels;

namespace NodeService.WebServer.Services.VirtualSystem
{
    public class ListDirectoryOptions
    {
        public string? SearchPattern { get; set; }

        public bool IncludeSubDirectories { get; set; }
    }


    public interface IVirtualFileSystem : IDisposable
    {
        ValueTask<string> GetWorkingDirectoryAsync(CancellationToken cancellationToken = default);

        ValueTask<string> SetWorkingDirectoryAsync(string workingDirectory, CancellationToken cancellationToken = default);

        ValueTask<Stream?> ReadFileAsync(string path,
            CancellationToken cancellationToken = default);

        ValueTask<Stream?> WriteFileAsync(
    string path,
    CancellationToken cancellationToken = default);

        IAsyncEnumerable<VirtualFileSystemInfo> ListDirectoryAsync(
            string directory,
            ListDirectoryOptions? options = null,
            CancellationToken cancellationToken = default);

        ValueTask<Exception?> DeleteDirectoryAsync(string directory,
            bool recursive = false,
            CancellationToken cancellationToken = default);

        ValueTask<Exception?> DeleteFileAsync(string path,
            CancellationToken cancellationToken = default);

        ValueTask<Exception?> ConnectAsync(CancellationToken cancellationToken = default);

        ValueTask<bool> DownloadStream(string path, Stream stream, CancellationToken cancellationToken = default);

        ValueTask<bool> UploadStream(string path, Stream stream, Infrastructure.DataModels.FtpFileExists remoteExists = Infrastructure.DataModels.FtpFileExists.Overwrite, CancellationToken cancellationToken = default);
        ValueTask<bool> FileExits(string path, CancellationToken cancellationToken = default);

        ValueTask<bool> DirectoryExits(string path, CancellationToken cancellationToken = default);
    }

    public abstract class VirtualFileSystemBase : IVirtualFileSystem
    {
        public abstract ValueTask<Exception?> DeleteDirectoryAsync(string directory,
            bool recursive = false,
            CancellationToken cancellationToken = default);

        public abstract ValueTask<Exception?> DeleteFileAsync(string file,
            CancellationToken cancellationToken = default);

        public abstract IAsyncEnumerable<VirtualFileSystemInfo> ListDirectoryAsync(string directory,
            ListDirectoryOptions? options = null,
            CancellationToken cancellationToken = default);

        public abstract ValueTask<string> GetWorkingDirectoryAsync(CancellationToken cancellationToken = default);

        public abstract ValueTask<string> SetWorkingDirectoryAsync(string workingDirectory,
            CancellationToken cancellationToken = default);

        public abstract ValueTask<Stream?> ReadFileAsync(string path,
            CancellationToken cancellationToken = default);

        public abstract ValueTask<Stream?> WriteFileAsync(string path,
            CancellationToken cancellationToken = default);

        public abstract void Dispose();
        public abstract ValueTask<Exception?> ConnectAsync(CancellationToken cancellationToken = default);
        public abstract ValueTask<bool> DownloadStream(string path, Stream stream, CancellationToken cancellationToken = default);
        public abstract ValueTask<bool> UploadStream(string path, Stream stream, FtpFileExists fileExists = FtpFileExists.Overwrite, CancellationToken cancellationToken = default);

        public abstract ValueTask<bool> FileExits(string path, CancellationToken cancellationToken = default);
        public abstract ValueTask<bool> DirectoryExits(string path, CancellationToken cancellationToken = default);
    }

    public class FtpVirtualFileSystem : VirtualFileSystemBase
    {
        private AsyncFtpClient _client;

        public FtpVirtualFileSystem(AsyncFtpClient asyncFtpClient)
        {
            _client = asyncFtpClient;
        }

        public override async ValueTask<Exception?> ConnectAsync(CancellationToken cancellationToken = default)
        {
            try
            {
                await _client.AutoConnect(cancellationToken);
                return null;
            }
            catch (Exception ex)
            {
                return ex;
            }

        }

        public override async ValueTask<Exception?> DeleteDirectoryAsync(
            string directory,
            bool recursive = false,
            CancellationToken cancellationToken = default)
        {
            try
            {
                await _client.DeleteDirectory(directory,
                    recursive ? FtpListOption.Recursive : FtpListOption.Auto,
                    cancellationToken);
                return null;
            }
            catch (Exception ex)
            {
                return ex;
            }
        }

        public override async ValueTask<Exception?> DeleteFileAsync(
            string path,
            CancellationToken cancellationToken = default)
        {
            try
            {
                await _client.DeleteFile(path, cancellationToken);
                return null;
            }
            catch (Exception ex)
            {

                return ex;
            }

        }

        public override ValueTask<bool> DirectoryExits(string path, CancellationToken cancellationToken = default)
        {
            return new ValueTask<bool>(_client.DirectoryExists(path, cancellationToken));
        }

        public override void Dispose()
        {
            _client.Dispose();
        }

        public override ValueTask<bool> DownloadStream(string path, Stream stream, CancellationToken cancellationToken = default)
        {
            return new ValueTask<bool>(_client.DownloadStream(stream, path, 0, null, cancellationToken, 0));
        }

        public override ValueTask<bool> FileExits(string path, CancellationToken cancellationToken = default)
        {
            return new ValueTask<bool>(_client.FileExists(path, cancellationToken));
        }

        public override async ValueTask<string> GetWorkingDirectoryAsync(CancellationToken cancellationToken = default)
        {
            var previousWorkingDirectory = await _client.GetWorkingDirectory(cancellationToken);
            return previousWorkingDirectory;
        }

        public override async IAsyncEnumerable<VirtualFileSystemInfo> ListDirectoryAsync(
            string directory,
            ListDirectoryOptions? options = null,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            await foreach (var item in _client.GetListingEnumerable(cancellationToken, cancellationToken))
            {
                yield return VirtualFileSystemInfo.FromFtpListItem(item);
            }
            yield break;
        }

        public override ValueTask<Stream?> ReadFileAsync(
            string path,
            CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public override async ValueTask<string> SetWorkingDirectoryAsync(
            string workingDirectory,
            CancellationToken cancellationToken = default)
        {
            var previousWorkingDirectory = await _client.GetWorkingDirectory(cancellationToken);
            await _client.SetWorkingDirectory(workingDirectory);
            return previousWorkingDirectory;
        }

        public override async ValueTask<bool> UploadStream(string path, Stream stream, FtpFileExists fileExists = FtpFileExists.Overwrite, CancellationToken cancellationToken = default)
        {
            return await _client.UploadStream(stream, path, FluentFTP.FtpRemoteExists.Overwrite, true, null, cancellationToken) == FtpStatus.Success;
        }

        public override ValueTask<Stream?> WriteFileAsync(
            string path,
            CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }
    }
}
